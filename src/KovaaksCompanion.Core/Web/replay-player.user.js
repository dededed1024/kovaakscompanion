// ==UserScript==
// @name         KovaaK's Companion replay player
// @namespace    kovaaks-companion
// @version      3
// @description  Adds a video icon next to every scenario you have a recorded replay of and plays the clip in a popup over the page.
// @match        __MATCHES__
// @grant        GM_getResourceText
// @grant        GM_xmlhttpRequest
// @resource     replays __WEB_DIR__/replays.js
// @run-at       document-idle
// ==/UserScript==

(() => {
  'use strict';
  const MARK = 'kcPlay';
  const VIDEO_ICON = '<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M4 6h10a2 2 0 0 1 2 2v1.6l4.4-2.6A.8.8 0 0 1 21.6 7.7v8.6a.8.8 0 0 1-1.2.7L16 14.4V16a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z"/></svg>';
  const OPEN_ICON = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 4h6v6"/><path d="M20 4l-9 9"/><path d="M18 14v4a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4"/></svg>';

  const norm = s => s.toLowerCase().replace(/\s+/g, ' ').trim();
  const byScenario = new Map(); // normalized scenario name -> replays, best score first

  // The app keeps this list in a file; the userscript manager reads it from disk each time a page loads.
  const match = (GM_getResourceText('replays') || '').match(/KC_REPLAYS=(\[[\s\S]*\]);/);
  if (match) {
    for (const r of JSON.parse(match[1])) {
      const k = norm(r.scenario);
      if (!byScenario.has(k)) byScenario.set(k, []);
      byScenario.get(k).push(r);
    }
    for (const list of byScenario.values()) list.sort((a, b) => b.score - a.score);
  }

  // Play buttons are found by text, so no site class names are needed.
  function scan() {
    if (byScenario.size === 0 || !document.body) return;
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    const hits = [];
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      const p = n.parentElement;
      if (!p || p.dataset[MARK] || /^(SCRIPT|STYLE|TEXTAREA|INPUT)$/.test(p.tagName)) continue;
      if (byScenario.has(norm(n.nodeValue))) hits.push(n);
    }
    for (const n of hits) {
      n.parentElement.dataset[MARK] = '1';
      const replays = byScenario.get(norm(n.nodeValue));
      const b = document.createElement('span');
      b.innerHTML = VIDEO_ICON;
      b.title = `Play replay (${replays.length})`;
      b.setAttribute('role', 'button');
      b.style.cssText = 'margin-left:6px;cursor:pointer;color:inherit;display:inline-flex;vertical-align:middle;user-select:none';
      b.addEventListener('click', e => { e.preventDefault(); e.stopPropagation(); open(replays); });
      n.parentNode.insertBefore(b, n.nextSibling);
    }
  }

  let timer = 0;
  new MutationObserver(() => { clearTimeout(timer); timer = setTimeout(scan, 300); })
    .observe(document.documentElement, { childList: true, subtree: true });
  scan();

  // ---- popup over the page; a shadow root keeps the site's styles out ------------------------------------------

  let host = null;
  let cleanup = () => {};

  function close() {
    cleanup();
    if (host) { host.remove(); host = null; document.removeEventListener('keydown', onKey, true); removeEventListener('resize', onResize); }
  }
  function onKey(e) { if (e.key === 'Escape') { e.stopPropagation(); close(); } }
  let onResize = () => {};

  function open(replays) {
    close();
    host = document.createElement('div');
    host.style.cssText = 'position:fixed;inset:0;z-index:2147483647';
    const root = host.attachShadow({ mode: 'closed' });
    root.innerHTML = `
      <style>
        .scrim{position:absolute;inset:0;background:rgba(0,0,0,.65);display:flex;align-items:center;justify-content:center;padding:16px;box-sizing:border-box;font:13px system-ui,sans-serif}
        .card{width:min(1180px,100%);background:#16181d;color:#e8eaf0;border-radius:12px;padding:8px 10px 10px;box-shadow:0 20px 60px #000}
        .bar{display:flex;gap:8px;align-items:center;padding-bottom:6px}
        .bar b{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:13px}
        .bar span{color:#8b90a0;font-size:12px}
        button{display:inline-flex;align-items:center;background:none;color:#8b90a0;border:0;border-radius:6px;padding:2px 6px;font:inherit;line-height:20px;cursor:pointer}
        button:hover{background:#262a33;color:#e8eaf0}
        .body{display:grid;grid-template-columns:minmax(0,1fr) 200px;gap:12px;align-items:start}
        .stage{position:relative;display:flex;justify-content:center}
        video{display:block;width:100%;aspect-ratio:16/9;background:#000;border-radius:8px}
        .note{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;text-align:center;padding:24px;color:#aeb3c2;background:rgba(0,0,0,.5);border-radius:8px}
        .note[hidden]{display:none}
        .note.err{color:#ff9a9a}
        .list{overflow-y:auto;scrollbar-width:thin}
        .row{display:grid;grid-template-columns:1fr auto;gap:12px;padding:4px 8px;cursor:pointer;white-space:nowrap}
        .row:hover{background:#21252d}
        .row.on{background:#1d2c42}
        .d{color:#aeb3c2}
        .n{text-align:right;font-variant-numeric:tabular-nums}
      </style>
      <div class="scrim"><div class="card">
        <div class="bar"><b></b><span></span><button class="app" title="Continue in the app"></button><button class="x">✕</button></div>
        <div class="body">
          <div class="stage"><video controls></video><div class="note" hidden></div></div>
          <div class="list"></div>
        </div>
      </div></div>`;
    const $ = s => root.querySelector(s);
    $('b').textContent = replays[0].scenario;
    $('.bar span').textContent = `${replays.length} ${replays.length === 1 ? 'replay' : 'replays'}`;
    $('.app').innerHTML = OPEN_ICON;
    const list = $('.list'), video = $('video'), stage = $('.stage'), note = $('.note');

    // The card is as tall as the video: the video takes the width of its column unless the window is too short for that, and the list never grows past it.
    function fit() {
      if (!video.videoWidth) return;
      const ratio = video.videoWidth / video.videoHeight;
      const room = innerHeight - 32 - 48; // scrim padding, header and card padding
      video.style.aspectRatio = String(ratio);
      video.style.width = Math.min(stage.clientWidth, Math.max(240, room * ratio)) + 'px';
      list.style.maxHeight = video.clientHeight + 'px';
    }
    video.addEventListener('loadedmetadata', fit);
    onResize = fit;
    addEventListener('resize', onResize);

    // A page cannot read a file address, so the userscript manager reads the clip from disk and the popup plays it from memory.
    let current = 0, request = null, blobUrl = null;
    function release() {
      if (request) { request.abort(); request = null; }
      if (blobUrl) { URL.revokeObjectURL(blobUrl); blobUrl = null; }
    }
    cleanup = release;
    function showNote(text, error) { note.textContent = text; note.className = 'note' + (error ? ' err' : ''); note.hidden = false; }

    function play(i) {
      release();
      current = i;
      [...list.children].forEach((row, k) => row.classList.toggle('on', k === i));
      video.removeAttribute('src'); video.load();
      showNote('Loading the video...');
      const mine = request = GM_xmlhttpRequest({
        method: 'GET',
        url: replays[i].video,
        responseType: 'blob',
        onload: r => {
          if (request !== mine) return;
          request = null;
          if (!r.response || !r.response.size) { showNote('The video file is empty or gone.', true); return; }
          blobUrl = URL.createObjectURL(r.response);
          video.src = blobUrl;
          note.hidden = true;
          video.play().catch(() => {});
        },
        onerror: () => {
          if (request !== mine) return;
          request = null;
          showNote('The video could not be read. In Tampermonkey, set Settings > Config mode: Advanced, then Security > "Allow scripts to access local files": All local files, and save.', true);
        },
      });
    }

    replays.forEach((r, i) => {
      const row = document.createElement('div');
      row.className = 'row';
      const d = document.createElement('span'), n = document.createElement('span');
      d.className = 'd'; n.className = 'n';
      d.textContent = new Date(r.end).toLocaleDateString();
      n.textContent = r.score.toLocaleString(undefined, { maximumFractionDigits: 1 });
      row.append(d, n);
      row.addEventListener('click', () => play(i));
      list.append(row);
    });

    $('.x').addEventListener('click', close);
    $('.app').addEventListener('click', () => {
      video.pause();
      location.href = `kovaaks-companion://open?id=${replays[current].id}&s=${video.currentTime}`;
    });
    $('.scrim').addEventListener('mousedown', e => { if (e.target === e.currentTarget) close(); });
    document.addEventListener('keydown', onKey, true);
    document.body.append(host);
    play(0);
  }
})();
