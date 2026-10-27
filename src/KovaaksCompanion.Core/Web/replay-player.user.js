// ==UserScript==
// @name         KovaaK's Companion replay player
// @namespace    kovaaks-companion
// @version      1
// @description  Adds a play button next to every scenario you have a recorded replay of.
// @match        https://evxl.app/*
// @match        https://www.evxl.app/*
// @match        https://app.voltaic.gg/*
// @grant        GM_xmlhttpRequest
// @connect      127.0.0.1
// @run-at       document-idle
// ==/UserScript==

(() => {
  'use strict';
  const BASE = 'http://127.0.0.1:__PORT__';
  const TOKEN = '__TOKEN__';
  const MARK = 'kcPlay';
  const VIDEO_ICON = '<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M4 6h10a2 2 0 0 1 2 2v1.6l4.4-2.6A.8.8 0 0 1 21.6 7.7v8.6a.8.8 0 0 1-1.2.7L16 14.4V16a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z"/></svg>';

  const norm = s => s.toLowerCase().replace(/\s+/g, ' ').trim();
  const byScenario = new Map(); // normalized scenario name -> replays, best score first

  function load() {
    GM_xmlhttpRequest({
      method: 'GET',
      url: `${BASE}/api/replays?t=${TOKEN}`,
      onload: r => {
        if (r.status !== 200) return;
        let list;
        try { list = JSON.parse(r.responseText); } catch { return; }
        byScenario.clear();
        for (const x of list) {
          const k = norm(x.scenario);
          if (!byScenario.has(k)) byScenario.set(k, []);
          byScenario.get(k).push(x);
        }
        for (const l of byScenario.values()) l.sort((a, b) => b.score - a.score);
        scan();
      },
      onerror: () => { /* the companion app is not running */ },
    });
  }

  // ---- play buttons: found by text, so no site class names are needed -------------------------------------------

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
      const p = n.parentElement;
      p.dataset[MARK] = '1';
      const replays = byScenario.get(norm(n.nodeValue));
      const b = document.createElement('span');
      b.innerHTML = VIDEO_ICON;
      b.title = `Play replay (${replays.length})`;
      b.setAttribute('role', 'button');
      b.style.cssText = 'margin-left:6px;cursor:pointer;color:#4da3ff;display:inline-flex;vertical-align:middle;user-select:none';
      b.addEventListener('click', e => { e.preventDefault(); e.stopPropagation(); open(replays); });
      n.parentNode.insertBefore(b, n.nextSibling);
    }
  }

  let timer = 0;
  new MutationObserver(() => { clearTimeout(timer); timer = setTimeout(scan, 300); })
    .observe(document.documentElement, { childList: true, subtree: true });

  // ---- popup over the page; a shadow root keeps the site's styles out ------------------------------------------

  let host = null;

  function close() {
    if (host) { host.remove(); host = null; document.removeEventListener('keydown', onKey, true); }
  }
  function onKey(e) { if (e.key === 'Escape') { e.stopPropagation(); close(); } }

  function open(replays) {
    close();
    host = document.createElement('div');
    host.style.cssText = 'position:fixed;inset:0;z-index:2147483647';
    const root = host.attachShadow({ mode: 'closed' });
    root.innerHTML = `
      <style>
        .scrim{position:absolute;inset:0;background:rgba(0,0,0,.65);display:flex;align-items:center;justify-content:center;font:14px system-ui,sans-serif}
        .card{width:min(1180px,94vw);background:#16181d;color:#e8eaf0;border-radius:12px;padding:8px 10px 10px;box-shadow:0 20px 60px #000}
        .bar{display:flex;gap:8px;align-items:center;margin-bottom:6px}
        .bar b{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:13px}
        .bar span{color:#8b90a0;font-size:12px}
        button{background:none;color:#8b90a0;border:0;border-radius:6px;padding:0 6px;font:inherit;line-height:20px;cursor:pointer}
        button:hover{background:#262a33;color:#e8eaf0}
        .body{display:grid;grid-template-columns:minmax(0,1fr) 300px;gap:12px;align-items:start}
        video{width:100%;max-height:75vh;background:#000;border-radius:8px}
        .list{max-height:75vh;overflow-y:auto;scrollbar-width:thin}
        .row{display:grid;grid-template-columns:1fr auto;gap:12px;padding:4px 8px;font-size:13px;cursor:pointer;white-space:nowrap}
        .row:hover{background:#21252d}
        .row.on{background:#1d2c42}
        .row .d{color:#aeb3c2;overflow:hidden;text-overflow:ellipsis}
        .row .n{text-align:right;font-variant-numeric:tabular-nums}
        .err{display:none;margin-top:8px;color:#ff9a9a}
        a{color:#4da3ff}
      </style>
      <div class="scrim"><div class="card">
        <div class="bar"><b></b><span></span><button>✕</button></div>
        <div class="body">
          <div>
            <video controls autoplay></video>
            <div class="err">The video could not be played here. <a target="_blank" rel="noopener">Open it in a new tab</a></div>
          </div>
          <div class="list"></div>
        </div>
      </div></div>`;
    const $ = s => root.querySelector(s);
    $('b').textContent = replays[0].scenario;
    $('.bar span').textContent = `${replays.length} ${replays.length === 1 ? 'replay' : 'replays'}`;
    const list = $('.list'), video = $('video'), err = $('.err'), link = $('a');
    const items = replays.map((r, i) => {
      const it = document.createElement('div');
      it.className = 'row';
      const d = document.createElement('span'), n = document.createElement('span');
      d.className = 'd'; n.className = 'n';
      d.textContent = new Date(r.end).toLocaleString();
      n.textContent = r.score.toLocaleString(undefined, { maximumFractionDigits: 1 });
      it.append(d, n);
      it.addEventListener('click', () => play(i));
      list.append(it);
      return it;
    });
    function play(i) {
      const url = `${BASE}/video/${replays[i].id}?t=${TOKEN}`;
      items.forEach((it, k) => it.classList.toggle('on', k === i));
      err.style.display = 'none';
      link.href = url;
      video.src = url;
    }
    video.addEventListener('error', () => { err.style.display = 'block'; });
    $('button').addEventListener('click', close);
    $('.scrim').addEventListener('mousedown', e => { if (e.target === e.currentTarget) close(); });
    document.addEventListener('keydown', onKey, true);
    document.body.append(host);
    play(0);
  }

  load();
  setInterval(() => { if (!document.hidden) load(); }, 60000);
})();
