// ==UserScript==
// @name         KovaaK's Companion replay player
// @namespace    kovaaks-companion
// @version      15
// @updateURL    __WEB_DIR__/replay-player.user.js
// @downloadURL  __WEB_DIR__/replay-player.user.js
// @description  Adds a video icon next to every scenario you have a recorded replay of and plays the clip in a popup over the page.
// @match        __MATCHES__
// @grant        GM_getResourceText
// @grant        unsafeWindow
// @resource    replays __WEB_DIR__/replays.js
// @run-at       document-idle
// ==/UserScript==

(() => {
  'use strict';
  const MARK = 'kcPlay';
  const VIDEO_ICON = '<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M4 6h10a2 2 0 0 1 2 2v1.6l4.4-2.6A.8.8 0 0 1 21.6 7.7v8.6a.8.8 0 0 1-1.2.7L16 14.4V16a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z"/></svg>';
  const OPEN_ICON = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 4h6v6"/><path d="M20 4l-9 9"/><path d="M18 14v4a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4"/></svg>';

  // The popup speaks the browser's language. The app writes its translations (its own language files) into replays.js; the English text is the key
  // and a missing entry shows the English text. F fills .NET style placeholders: {0}, {0:0}, {0:0.00} and {0:P0}.
  let words = {};
  const T = s => words[s] ?? s;
  const F = (s, ...a) => T(s).replace(/\{(\d+)(?::([^}]*))?\}/g, (_, i, f) => f === 'P0' ? Math.round(a[+i] * 100) + '%' : f ? a[+i].toFixed((f.split('.')[1] || '').length) : String(a[+i]));

  const norm = s => s.toLowerCase().replace(/\s+/g, ' ').trim();
  const byScenario = new Map(); // normalized scenario name -> replays, best score first

  // The app keeps this list in a file; the userscript manager reads it from disk each time a page loads.
  let videoDir = ''; // the folder the clips are in, shown when the user is asked to pick it
  let runsBy = {}; // lower-case scenario name -> its local runs (see RUN)
  const match = (GM_getResourceText('replays') || '').match(/KC_REPLAYS=(\{[\s\S]*\});/);
  if (match) {
    const data = JSON.parse(match[1]);
    videoDir = data.dir;
    const lang = ((navigator.languages && navigator.languages[0]) || navigator.language || 'en').slice(0, 2).toLowerCase();
    words = (data.i18n || {})[lang] || {};
    runsBy = data.runs || {};
    for (const r of data.items) {
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
      const title = F('Play replay ({0})', replays.length);
      const onClick = e => { e.preventDefault(); e.stopPropagation(); open(replays); };
      // Where the row has its own action buttons (play, leaderboard), the video button joins them and copies their look.
      const ref = [...(n.parentElement.closest('tr, li, [role=row]')?.querySelectorAll('.action-button') ?? [])].pop();
      const bar = ref?.parentElement?.parentElement;
      if (ref && bar) {
        const size = ref.querySelector('svg')?.getBoundingClientRect().width || 16;
        const b = ref.cloneNode(false);
        b.removeAttribute('href');
        b.setAttribute('role', 'button');
        b.style.cursor = 'pointer';
        b.setAttribute('aria-label', title);
        b.title = title;
        b.innerHTML = VIDEO_ICON.replace('width="16" height="16"', `width="${size}" height="${size}"`);
        b.addEventListener('click', onClick);
        bar.prepend(b);
        continue;
      }
      const b = document.createElement('span');
      b.innerHTML = VIDEO_ICON.replace('width="16" height="16"', 'width="1.1em" height="1.1em" style="display:block"');
      b.title = title;
      b.setAttribute('role', 'button');
      b.style.cssText = 'margin-left:6px;cursor:pointer;color:inherit;display:inline-block;vertical-align:-.2em;line-height:0;user-select:none';
      b.addEventListener('click', onClick);
      n.parentNode.insertBefore(b, n.nextSibling);
    }
  }

  let timer = 0;
  new MutationObserver(() => { clearTimeout(timer); timer = setTimeout(scan, 300); })
    .observe(document.documentElement, { childList: true, subtree: true });
  scan();

  // ---- the videos folder: the user picks it once per site and the browser remembers it -------------------------

  // A page cannot read a file address, but a folder the user picked is readable, and a file from it plays without being read first.
  const W = window.showDirectoryPicker ? window : unsafeWindow;
  const DB = 'kovaaks-companion', STORE = 'handles', KEY = 'videos';

  function store(mode, run) {
    return new Promise((resolve, reject) => {
      const open = W.indexedDB.open(DB, 1);
      open.onupgradeneeded = () => open.result.createObjectStore(STORE);
      open.onerror = () => reject(open.error);
      open.onsuccess = () => {
        try {
          const req = run(open.result.transaction(STORE, mode).objectStore(STORE));
          req.onsuccess = () => { open.result.close(); resolve(req.result); };
          req.onerror = () => reject(req.error);
        } catch (e) { open.result.close(); reject(e); } // e.g. a handle the browser cannot store
      };
    });
  }

  // The remembered folder, or null when none is saved or the browser did not get its permission back (that asks, so it has to follow a click).
  let picked = null; // the folder picked on this page, kept even where the browser cannot save it
  async function savedFolder() {
    const h = picked || await store('readonly', s => s.get(KEY)).catch(() => null);
    if (!h) return null;
    const opts = { mode: 'read' };
    return (await h.queryPermission(opts)) === 'granted' || (await h.requestPermission(opts)) === 'granted' ? h : null;
  }

  // The dialog opens where the folder was last picked: the saved handle (even one whose permission is gone) tells the browser where that is.
  async function pickFolder() {
    const last = await store('readonly', s => s.get(KEY)).catch(() => null);
    const h = await W.showDirectoryPicker(last ? { id: 'kc-videos', mode: 'read', startIn: last } : { id: 'kc-videos', mode: 'read' });
    picked = h;
    await store('readwrite', s => s.put(h, KEY)).catch(() => {});
    return h;
  }

  async function clipIn(folder, path) {
    const parts = path.split('/');
    let dir = folder;
    for (const p of parts.slice(0, -1)) dir = await dir.getDirectoryHandle(p);
    return (await dir.getFileHandle(parts[parts.length - 1])).getFile();
  }

  // ---- run statistics: the file the app wrote for each replay (cache/<id>.cache in the picked folder), and what is drawn from it ----

  const COLOR = { accent: '#0a84ff', green: '#30d158', orange: '#ff9f0a', red: '#ff453a', purple: '#bf5af2', teal: '#64d2ff', yellow: '#ffd60a' };
  const SPEED_COLOR = { mine: '#5693e0', top: '#57b578', avg: '#5693e0', bottom: '#d6726c' }; // the speed card has many bars, so its colors are softer than the charts'
  const idKey = r => r.id.replace(/-/g, '').toLowerCase();
  const statsCache = new Map(); // replay id -> its statistics file, kept for as long as the page is open
  const cachedStats = async (folder, r) => { if (!statsCache.has(r.id)) statsCache.set(r.id, await statsOf(folder, r)); return statsCache.get(r.id); };
  const statsOf = async (folder, r) => JSON.parse(await (await clipIn(folder, 'cache/' + idKey(r) + '.cache')).text());
  const dash = '–';
  const num = (v, d = 1) => v == null ? dash : v.toLocaleString(undefined, { maximumFractionDigits: d });
  const pct = (v, d = 1) => v == null ? dash : (v * 100).toFixed(d) + '%';
  const clock = s => { s = Math.max(0, Math.floor(s)); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`; };
  const span = s => s >= 3600 ? `${Math.floor(s / 3600)}h ${Math.floor(s / 60) % 60}m` : `${Math.floor(s / 60)}m ${Math.floor(s % 60)}s`;

  // The local runs of the scenario that the app listed in replays.js: end, score, accuracy, kills, TTK, overshots, reloads, distance, cm/360, seconds, shots.
  const RUN = { end: 0, score: 1, acc: 2, kills: 3, ttk: 4, over: 5, reloads: 6, dist: 7, cm: 8, secs: 9, shots: 10 };
  const mean = (rows, i) => { const v = rows.map(r => r[i]).filter(x => x != null); return v.length ? v.reduce((a, b) => a + b, 0) / v.length : null; };

  // Two side-by-side tables: this run against the mean of all the scenario's local runs, green when this run is better and red when worse.
  function renderTable(el, replay, st, rows) {
    const s = st.summary || {};
    const over = s.overshots != null && s.kills ? s.overshots / s.kills : null;
    const withKills = rows.filter(r => r[RUN.over] != null && r[RUN.kills]);
    const overAvg = withKills.length ? withKills.reduce((a, r) => a + r[RUN.over], 0) / withKills.reduce((a, r) => a + r[RUN.kills], 0) : null;
    const rank = rows.length ? `${1 + rows.filter(r => r[RUN.score] > replay.score).length} / ${rows.length}` : dash;
    // label, this run, mean, format, whether a higher value is better (null: no color)
    const lines = [
      ['Played', new Date(replay.end).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }), null, v => v, null],
      ['Score', replay.score, mean(rows, RUN.score), v => num(v, 1), null],
      ['Rank', rank, null, v => v, null],
      ['Accuracy', s.accuracy, mean(rows, RUN.acc), v => pct(v), true],
      ['Kills', s.kills, mean(rows, RUN.kills), v => num(v, 1), true],
      ['TTK', s.avgTtk, mean(rows, RUN.ttk), v => v == null ? dash : v.toFixed(2) + 's', false],
      ['Overshots', s.overshots, mean(rows, RUN.over), v => num(v, 1), false],
      ['Overshots / kill', over, overAvg, v => v == null ? dash : v.toFixed(2), false],
      ['Reloads', s.reloads, mean(rows, RUN.reloads), v => num(v, 1), false],
      ['Distance moved', s.distance, mean(rows, RUN.dist), v => num(v, 2), true],
      ['Sensitivity', s.cm360, null, v => v == null ? dash : num(v, 2) + ' cm/360', null],
      ['Time', st.duration, null, v => v == null ? dash : span(v), null],
    ];
    const half = Math.ceil(lines.length / 2);
    const cell = ([label, v, avg, f, up]) => {
      const tone = up == null || v == null || avg == null || v === avg ? '' : (v > avg) === up ? COLOR.green : COLOR.red;
      return `<div class="k">${T(label)}</div><div class="v"${tone ? ` style="color:${tone}"` : ''}>${f(v)}</div><div class="a">${avg == null ? '' : f(avg)}</div>`;
    };
    const table = part => `<div class="tab"><div class="k"></div><div class="h">${T('This run')}</div><div class="h">${T('Average')}</div>${part.map(cell).join('')}</div>`;
    el.innerHTML = table(lines.slice(0, half)) + table(lines.slice(half));
  }

  // Monotone cubic curve through the points (Fritsch-Carlson), as an SVG path.
  function curve(p) {
    const n = p.length;
    if (n === 0) return '';
    if (n === 1) return `M${p[0][0]},${p[0][1]}`;
    const dx = [], m = [], t = [];
    for (let i = 0; i < n - 1; i++) { dx[i] = p[i + 1][0] - p[i][0]; m[i] = dx[i] === 0 ? 0 : (p[i + 1][1] - p[i][1]) / dx[i]; }
    t[0] = m[0]; t[n - 1] = m[n - 2];
    for (let i = 1; i < n - 1; i++) t[i] = m[i - 1] * m[i] <= 0 ? 0 : (m[i - 1] + m[i]) / 2;
    for (let i = 0; i < n - 1; i++) {
      if (m[i] === 0) { t[i] = 0; t[i + 1] = 0; continue; }
      const a = t[i] / m[i], b = t[i + 1] / m[i], s = a * a + b * b;
      if (s > 9) { const k = 3 / Math.sqrt(s); t[i] = k * a * m[i]; t[i + 1] = k * b * m[i]; }
    }
    let d = `M${p[0][0]},${p[0][1]}`;
    for (let i = 0; i < n - 1; i++) { const h = dx[i] / 3; d += `C${p[i][0] + h},${p[i][1] + t[i] * h} ${p[i + 1][0] - h},${p[i + 1][1] - t[i + 1] * h} ${p[i + 1][0]},${p[i + 1][1]}`; }
    return d;
  }

  const niceCeil = v => { if (!(v > 0)) return 1; const p = 10 ** Math.floor(Math.log10(v)); return [1, 2, 2.5, 5, 10].map(m => m * p).find(x => v <= x) || 10 * p; };
  const compact = v => Math.abs(v) >= 1000 ? `${+(v / 1000).toFixed(2)}k` : String(+v.toFixed(1));

  // The running totals per second that the chart draws (accuracy, score, kills, shots, hits) and the mouse speed of each second; a run saved without per-second data has one point per kill.
  function seriesOf(st) {
    const out = { t: [], acc: [], score: [], kills: [], shots: [], hits: [], speed: [] };
    const k = st.kills;
    if (st.perf) {
      let sh = 0, hi = 0, ki = 0, sc = 0;
      st.perf.t.forEach((t, i) => {
        sh += st.perf.shots[i]; hi += st.perf.hits[i]; ki += st.perf.kills[i]; sc += st.perf.score[i];
        out.t.push(t); out.acc.push(sh ? hi / sh : 0); out.score.push(sc); out.kills.push(ki); out.shots.push(sh); out.hits.push(hi);
        out.speed.push(st.perf.speed ? st.perf.speed[i] : null);
      });
    } else if (k) {
      let sh = 0, hi = 0;
      k.t.forEach((t, i) => {
        sh += k.shots[i]; hi += k.hits[i];
        out.t.push(t); out.acc.push(sh ? hi / sh : 0); out.score.push(null); out.kills.push(i + 1); out.shots.push(sh); out.hits.push(hi); out.speed.push(null);
      });
    }
    return out;
  }

  const SERIES = [
    { key: 'acc', name: 'Accuracy', color: COLOR.accent, pct: true, fmt: v => pct(v, 0) },
    { key: 'score', name: 'Score', color: COLOR.green, fmt: v => num(v, 0) },
    { key: 'kills', name: 'Kills', color: COLOR.orange, fmt: v => num(v, 0) },
    { key: 'shots', name: 'Shots', color: COLOR.purple, fmt: v => num(v, 0) },
    { key: 'hits', name: 'Hits', color: COLOR.teal, fmt: v => num(v, 0) },
    { key: 'speed', name: 'Mouse speed', color: COLOR.yellow, fmt: v => num(v, 0) + '°/s' },
  ];
  const visibleKey = 'kc-series';
  const loadVisible = () => { try { const v = JSON.parse(localStorage.getItem(visibleKey)); if (Array.isArray(v)) return new Set(v); } catch { /* none saved */ } return new Set(['acc', 'score']); };

  // The run chart: one curve per shown figure over the video's time, a playhead that follows the video (and seeks it when dragged), a tick at every kill, and the figures at the playhead on the chips.
  function makeChart(chipsEl, svg, video) {
    let st = null, data = null, offset = 0;
    const shown = loadVisible();
    let layout = null; // per shown series: its scale, rebuilt on every draw
    const H = 150, PAD = 4;
    const timeOf = t => t + offset;
    const lastT = () => timeOf(data.t[data.t.length - 1] || 1);
    const duration = () => isFinite(video.duration) && video.duration > 0 ? video.duration : Math.max(1, lastT());
    const at = (key, t) => { // the figure at video time t: held after the last point, none before the first
      const v = data[key], ts = data.t;
      if (!ts.length || timeOf(ts[0]) > t) return null;
      let i = ts.length - 1;
      while (i > 0 && timeOf(ts[i]) > t) i--;
      if (i === ts.length - 1 || v[i] == null || v[i + 1] == null) return v[i];
      const a = timeOf(ts[i]), b = timeOf(ts[i + 1]);
      return b === a ? v[i] : v[i] + (v[i + 1] - v[i]) * (t - a) / (b - a);
    };

    function chips() {
      chipsEl.innerHTML = '';
      for (const s of SERIES) {
        if (!data[s.key].some(v => v != null)) continue;
        const c = document.createElement('button');
        c.className = 'chip' + (shown.has(s.key) ? '' : ' off');
        c.innerHTML = `<i style="background:${s.color}"></i>${T(s.name)} <b data-k="${s.key}">${dash}</b>`;
        c.addEventListener('click', () => {
          if (shown.has(s.key)) shown.delete(s.key); else shown.add(s.key);
          try { localStorage.setItem(visibleKey, JSON.stringify([...shown])); } catch { /* not saved */ }
          draw();
        });
        chipsEl.append(c);
      }
    }

    function draw() {
      if (!data) return;
      const W = svg.clientWidth || 600, dur = duration();
      const X = t => Math.min(W, Math.max(0, timeOf(t) / dur * W));
      const plot = H - PAD * 2;
      const active = SERIES.filter(s => shown.has(s.key) && data[s.key].some(v => v != null));
      layout = [];
      let out = `<line x1="0" x2="${W}" y1="${PAD}" y2="${PAD}" class="g"/><line x1="0" x2="${W}" y1="${H / 2}" y2="${H / 2}" class="g"/><line x1="0" x2="${W}" y1="${H - PAD}" y2="${H - PAD}" class="g"/>`;
      let col = 0;
      active.forEach((s, idx) => {
        const vals = data[s.key];
        const idxs = vals.map((v, i) => v == null ? -1 : i).filter(i => i >= 0);
        let lo = 0, hi;
        if (s.pct) {
          const mn = Math.min(...idxs.map(i => vals[i])), mx = Math.max(...idxs.map(i => vals[i]));
          lo = Math.max(0, Math.floor((mn - .02) * 20) / 20); hi = Math.min(1, Math.ceil((mx + .02) * 20) / 20);
          if (hi - lo < .2) { hi = Math.min(1, lo + .2); lo = Math.max(0, hi - .2); }
        } else hi = niceCeil(Math.max(...idxs.map(i => vals[i])));
        const Y = v => PAD + plot * (1 - (v - lo) / (hi - lo || 1));
        const pts = idxs.map(i => [X(data.t[i]), Y(vals[i])]);
        layout.push({ s, Y });
        const d = curve(pts);
        if (idx === 0 && pts.length > 1) out += `<path d="${d}L${pts[pts.length - 1][0]},${H - PAD}L${pts[0][0]},${H - PAD}Z" fill="${s.color}" opacity=".16"/>`;
        out += `<path d="${d}" fill="none" stroke="${s.color}" stroke-width="${s.pct ? 2 : 1.5}" stroke-linejoin="round"/>`;
        out += [0, .5, 1].map(f => `<text y="${Y(lo + (hi - lo) * f) + (f === 0 ? -2 : f === 1 ? 9 : 3)}" ${s.pct ? 'x="4"' : `x="${W - 4 - col * 34}" text-anchor="end"`} fill="${s.color}">${s.pct ? Math.round((lo + (hi - lo) * f) * 100) + '%' : compact(lo + (hi - lo) * f)}</text>`).join('');
        if (!s.pct) col++;
      });
      if (st.kills) for (const t of st.kills.t) out += `<rect x="${X(t) - 1}" y="${H - PAD - 6}" width="2" height="6" rx="1" fill="${COLOR.red}"/>`;
      out += `<g class="ph"><line y1="0" y2="${H}" stroke="#1d3ab8" stroke-width="2"/>${layout.map(l => `<circle r="3.5" fill="${l.s.color}" stroke="#fff" stroke-width="1.5"/>`).join('')}</g>`;
      svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
      svg.innerHTML = out;
      chips();
      update();
    }

    // Follows the video: the playhead, the dot on every curve and the figures on the chips.
    function update() {
      if (!data || !layout) return;
      const W = svg.clientWidth || 600;
      const x = Math.min(W, Math.max(0, video.currentTime / duration() * W));
      const ph = svg.querySelector('.ph');
      if (ph) {
        ph.firstChild.setAttribute('x1', x); ph.firstChild.setAttribute('x2', x);
        layout.forEach((l, i) => {
          const v = at(l.s.key, video.currentTime), dot = ph.children[i + 1];
          dot.setAttribute('cx', x); dot.setAttribute('cy', v == null ? -10 : l.Y(v));
        });
      }
      for (const b of chipsEl.querySelectorAll('b')) {
        const s = SERIES.find(q => q.key === b.dataset.k), v = at(s.key, video.currentTime);
        b.textContent = v == null ? dash : s.fmt(v);
      }
    }

    function seek(e) {
      const r = svg.getBoundingClientRect();
      if (isFinite(video.duration) && video.duration > 0) video.currentTime = Math.min(video.duration, Math.max(0, (e.clientX - r.left) / r.width * video.duration));
      update();
    }
    let dragging = false;
    svg.addEventListener('pointerdown', e => { dragging = true; svg.setPointerCapture(e.pointerId); seek(e); });
    svg.addEventListener('pointermove', e => { if (dragging) seek(e); });
    svg.addEventListener('pointerup', () => { dragging = false; });

    return {
      show(stats) { st = stats; data = seriesOf(stats); offset = stats.offset || 0; draw(); return data.t.length > 0; },
      clear() { st = data = layout = null; chipsEl.innerHTML = ''; svg.innerHTML = ''; },
      draw, update,
    };
  }

  // ---- mouse speed card: this run against the best and worst runs of the scenario ----

  // The order of the figures in a run's speed row (replays.js) and the way each is shown: label, row index, unit, format.
  // 'cm' turns deg/s into cm/s at the run's cm/360, 'cmacc' does the same for deg/s², 'ms' and 'pct' scale seconds and ratios, 'k' divides by 1000.
  const SPEED = {
    General: [['Average speed', 0, 'cm', 1, ' cm/s'], ['Max speed', 1, 'cm', 1, ' cm/s'], ['Firing speed', 2, 'cm', 1, ' cm/s'], ['Non-firing speed', 3, 'cm', 1, ' cm/s'], ['Time between kills', 4, 'ms', 0, ' ms'], ['Firing time', 5, 'pct', 0, '%'], ['Direction changes', 6, '', 1, '/s']],
    Tracking: [['Speed variation', 7, 'pct', 0, '%'], ['Jerk', 8, 'k', 1, ' k°/s³'], ['Acceleration variation', 9, 'cmacc', 0, ' cm/s²']],
    Flick: [['Peak flick speed', 10, 'cm', 1, ' cm/s'], ['Flick time', 11, 'ms', 0, ' ms'], ['Click delay after stop', 12, 'ms', 0, ' ms'], ['Reaction time', 13, 'ms', 0, ' ms'], ['Path efficiency', 14, 'pct', 0, '%'], ['Ballistic ratio', 15, 'pct', 0, '%'], ['Flick distance', 16, '', 1, '°']],
  };
  const SPEED_KEYS = ['mean', 'max', 'fire', 'idle', 'killInterval', 'hold', 'reversals', 'speedCv', 'jerk', 'accelStd', 'flickPeak', 'flickTime', 'stopToClick', 'reaction', 'efficiency', 'ballistic', 'flickDistance'];

  function speedValue(row, spec, cm) {
    const v = row[spec[1]];
    if (v == null) return null;
    switch (spec[2]) {
      case 'cm': case 'cmacc': return cm > 0 ? v * cm / 360 : null;
      case 'ms': return v * 1000;
      case 'pct': return v * 100;
      case 'k': return v / 1000;
      default: return v;
    }
  }

  // The kind of aiming the scenario asks for, from its name, else from how the runs were played (holding the button most of the time is tracking).
  function aimKind(scenario, speedRows) {
    const n = scenario.toLowerCase();
    if (/track|strafe|smooth|pasu/.test(n)) return 'Tracking';
    if (/switch|6targets|multi/.test(n)) return 'Switching';
    if (/flick|1wall|pop/.test(n)) return 'Flick';
    const holds = speedRows.map(r => r[5]).filter(v => v != null);
    return holds.length && holds.reduce((a, b) => a + b, 0) / holds.length >= .5 ? 'Tracking' : 'Switching';
  }

  function renderSpeed(el, replay, st, rows) {
    const sp = st.speed;
    if (!sp) { el.hidden = true; return; }
    const own = SPEED_KEYS.map(k => sp[k] ?? null);
    const cm0 = st.summary && st.summary.cm360;
    const withSpeed = rows.filter(r => r[11]).sort((a, b) => b[RUN.score] - a[RUN.score]);
    const edge = Math.max(1, Math.ceil(withSpeed.length * .05));
    const mean0 = group => spec => {
      const vals = group.map(r => speedValue(r[11], spec, r[RUN.cm])).filter(v => v != null);
      return vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : null;
    };
    // top to bottom, so the line of a figure runs from the best plays through this run to the worst
    const lines = [{ name: 'This run', color: SPEED_COLOR.mine, get: spec => speedValue(own, spec, cm0) }];
    if (withSpeed.length > 1) {
      lines.unshift({ name: 'Top 5%', color: SPEED_COLOR.top, get: mean0(withSpeed.slice(0, edge)) });
      lines.push({ name: 'Bottom 5%', color: SPEED_COLOR.bottom, get: mean0(withSpeed.slice(-edge)) });
    }
    speedCard(el, replay.scenario, withSpeed, lines);
  }

  function speedCard(el, scenario, withSpeed, lines) {
    const kind = aimKind(scenario, withSpeed.map(r => r[11]));
    const order = ['General', kind === 'Tracking' ? 'Tracking' : 'Flick', kind === 'Tracking' ? 'Flick' : 'Tracking'];
    // One bar per figure: each line (top, this run, bottom) fills its own thin strip from the left up to its value (the strips are stacked, so equal values do not hide each other), and the values sit at the right. The row's lowest and highest value take 70% of the bar's width, centered; when they are within 28% of the mean the bar covers the mean +-20% instead, so close values stay close together.
    const line = (vals, spec) => {
      const present = vals.filter(v => v != null), hi = Math.max(...present), lo = Math.min(...present);
      const mean = present.reduce((a, b) => a + b, 0) / present.length, centre = (hi + lo) / 2, width = Math.max((hi - lo) / .7, .4 * Math.abs(mean));
      const x = v => width ? Math.min(97, Math.max(3, 50 + (v - centre) / width * 100)) : 50; // percent of the bar's width
      const fills = vals.map((v, i) => v == null ? '<i></i>' : `<i style="width:${x(v)}%;background:${lines[i].color}"></i>`).join('');
      const nums = vals.map((v, i) => `<b style="color:${v == null ? 'inherit' : lines[i].color}">${v == null ? '' : num(v, spec[3]) + spec[4]}</b>`).join('');
      return `<div class="sv"><div class="trk">${fills}</div><div class="nums" style="grid-template-columns:repeat(${vals.length},72px)">${nums}</div></div>`;
    };
    let html = `<div class="ctitle">${T('Mouse speed')}<span class="legend">${lines.map(l => `<span><i style="background:${l.color}"></i>${T(l.name)}</span>`).join('')}</span></div>`;
    const parts = [];
    for (const group of order) {
      let body = '';
      for (const spec of SPEED[group]) {
        const vals = lines.map(l => l.get(spec));
        if (vals.every(v => v == null)) continue;
        body += `<div class="sl">${T(spec[0])}</div>${line(vals, spec)}`;
      }
      if (body) parts.push(`<div class="sg">${T(group)}</div><div class="sgrid">${body}</div>`);
    }
    // two columns: the general figures on the left, the other groups under each other on the right
    el.innerHTML = html + `<div class="scols"><div>${parts[0] || ''}</div><div>${parts.slice(1).join('')}</div></div>`;
    el.hidden = false;
  }

  // ---- trail: the last second of the camera path over the video, drawn from the statistics file ----

  const hsv = h => { const r = h < 60 ? 255 : 255 * (1 - (h - 60) / 60), g = h < 60 ? 255 * h / 60 : 255; return `${Math.round(r)},${Math.round(g)},0`; };

  function makeTrail(canvas, video) {
    let st = null;
    const ctx = canvas.getContext('2d');
    let on = true;
    try { on = localStorage.getItem('kc-trail') !== '0'; } catch { /* default */ }

    const dirAt = (tr, t) => { // camera direction at run time t
      const f = Math.min(tr.yaw.length - 1, Math.max(0, (t - tr.t0) * tr.hz)), i = Math.floor(f), j = Math.min(tr.yaw.length - 1, i + 1), w = f - i;
      return { yaw: tr.yaw[i] + (tr.yaw[j] - tr.yaw[i]) * w, pitch: tr.pitch[i] + (tr.pitch[j] - tr.pitch[i]) * w };
    };

    // Where a past camera direction lies in the current view (normalized device coordinates), or null when it is behind the camera.
    const project = (past, cur, hfov, aspect) => {
      const r = Math.PI / 180, dy = (past.yaw - cur.yaw) * r, p = past.pitch * r, cp = cur.pitch * r;
      const x = Math.cos(p) * Math.sin(dy), y = Math.sin(p), z = Math.cos(p) * Math.cos(dy);
      const y2 = y * Math.cos(cp) - z * Math.sin(cp), z2 = y * Math.sin(cp) + z * Math.cos(cp);
      if (z2 <= 1e-9) return null;
      const tanH = Math.tan(hfov * r / 2);
      return [x / z2 / tanH, y2 / z2 / (tanH / aspect)];
    };

    // Hit shots make the trail green, misses red; while the button is held the held accuracy colors it from red to green; otherwise it is grey.
    const colorOf = (tr, t0, t1) => {
      let miss = false;
      for (const [ft, o] of tr.fires || []) {
        if (ft < t0 - .04 || ft > t1 + .04) continue;
        if (o === 1) return '0,255,0';
        if (o === 2) miss = true;
      }
      if (miss) return '255,0,0';
      const h = tr.held && tr.held[Math.round((t0 + t1) / 2 * 20)];
      return h == null ? '142,142,147' : hsv(h * 120);
    };

    function draw() {
      const w = canvas.clientWidth, h = canvas.clientHeight, dpr = devicePixelRatio || 1;
      if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) { canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr); }
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.clearRect(0, 0, w, h);
      const tr = st && st.trajectory;
      if (!on || !tr || !tr.view || !video.videoWidth) return;
      const t = video.currentTime - (st.offset || 0);
      if (t < 0 || t > st.duration + .5) return;
      // the video is letterboxed inside its element; the trail is drawn on the picture only
      const ratio = video.videoWidth / video.videoHeight;
      let vw = w, vh = w / ratio;
      if (vh > h) { vh = h; vw = h * ratio; }
      const ox = (w - vw) / 2, oy = (h - vh) / 2;
      const cur = dirAt(tr, t), from = Math.max(0, t - 1);
      const pts = [{ t: from, d: dirAt(tr, from) }];
      for (let i = Math.floor((from - tr.t0) * tr.hz) + 1, n = tr.yaw.length; i < n; i++) {
        const ts = tr.t0 + i / tr.hz;
        if (ts >= t) break;
        if (ts > from) pts.push({ t: ts, d: { yaw: tr.yaw[i], pitch: tr.pitch[i] } });
      }
      pts.push({ t, d: cur });
      ctx.save();
      ctx.beginPath(); ctx.rect(ox, oy, vw, vh); ctx.clip();
      ctx.lineWidth = 3.5; ctx.lineCap = 'round';
      let prev = null;
      for (const p of pts) {
        const n = project(p.d, cur, tr.view.hfov, tr.view.aspect);
        const q = n && [ox + (n[0] + 1) / 2 * vw, oy + (1 - n[1]) / 2 * vh];
        if (prev && prev.q && q) {
          const alpha = Math.min(1, Math.max(0, 1 - (t - (prev.t + p.t) / 2)));
          ctx.strokeStyle = `rgba(${colorOf(tr, prev.t, p.t)},${alpha})`;
          ctx.beginPath(); ctx.moveTo(prev.q[0], prev.q[1]); ctx.lineTo(q[0], q[1]); ctx.stroke();
        }
        prev = { t: p.t, q };
      }
      ctx.restore();
    }

    return {
      show(stats) { st = stats; draw(); return !!(stats.trajectory && stats.trajectory.view); },
      clear() { st = null; draw(); },
      draw,
      get on() { return on; },
      toggle() { on = !on; try { localStorage.setItem('kc-trail', on ? '1' : '0'); } catch { /* not saved */ } draw(); return on; },
    };
  }

  // ---- compare chart: this run against the average and the best run of the scenario, second by second ----

  const CMP = [
    { key: 'score', name: 'Score', color: COLOR.green, digits: 1 },
    { key: 'acc', name: 'Accuracy', color: COLOR.accent, pct: true },
    { key: 'kills', name: 'Kills', color: COLOR.orange, digits: 0 },
    { key: 'hits', name: 'Hits', color: COLOR.teal, digits: 0 },
    { key: 'shots', name: 'Shots', color: COLOR.purple, digits: 0 },
    { key: 'reloads', name: 'Reloads', color: '#ff6482', digits: 0 },
    { key: 'dist', name: 'Distance moved', color: '#66d4cf', digits: 2 },
  ];

  // The running totals of a replay's per-second data, one entry per bucket.
  function runningOf(st) {
    const p = st.perf;
    if (!p) return null;
    const out = { t: p.t, score: [], acc: [], kills: [], hits: [], shots: [], reloads: [], dist: [] };
    let sc = 0, ki = 0, hi = 0, sh = 0, re = 0, di = 0;
    p.t.forEach((_, i) => {
      sc += p.score[i]; ki += p.kills[i]; hi += p.hits[i]; sh += p.shots[i]; re += p.reloads ? p.reloads[i] : 0; di += p.distance ? p.distance[i] : 0;
      out.score.push(sc); out.kills.push(ki); out.hits.push(hi); out.shots.push(sh); out.acc.push(sh ? hi / sh : 0); out.reloads.push(re); out.dist.push(di);
    });
    return out;
  }

  // The running value at whole second `sec`: 0 before the first bucket, nothing once the run is over.
  function runningAt(run, key, sec) {
    const ts = run.t;
    if (!ts.length || sec > ts[ts.length - 1] + 1) return NaN;
    let i = -1;
    for (let k = 0; k < ts.length && ts[k] <= sec; k++) i = k;
    return i < 0 ? 0 : run[key][i];
  }

  function makeCompare(el) {
    let runs = null, metric = 'score', title = '';
    const show = hidden => { el.hidden = hidden; };

    function render() {
      if (!runs) { show(true); return; }
      show(false); // measured below, so it has to be visible
      const m = CMP.find(c => c.key === metric);
      const fmt = (v, tip) => v == null || !isFinite(v) ? dash : m.pct ? pct(v, tip ? 1 : 0) : num(v, m.digits);
      const secs = Math.floor(Math.max(...runs.map(r => r.run.t[r.run.t.length - 1])));
      const series = runs.map(r => Array.from({ length: secs + 1 }, (_, s) => runningAt(r.run, metric, s)));
      const mine = series[0];
      const avg = Array.from({ length: secs + 1 }, (_, s) => { const v = series.map(x => x[s]).filter(isFinite); return v.length ? v.reduce((a, b) => a + b, 0) / v.length : NaN; });
      const bestIdx = runs.findIndex(r => r.best);
      const best = bestIdx > 0 ? series[bestIdx] : null;
      const finite = a => a.filter(isFinite);
      const all = [...finite(mine), ...finite(avg), ...(best ? finite(best) : [])];
      if (secs < 2 || !all.length) { el.innerHTML = `<div class="ctitle">${T('Compare')}</div><div class="none">${T('No data')}</div>`; show(false); return; }
      let lo = Math.min(...all), hi = Math.max(...all);
      const pad = (hi - lo) * .06 || (m.pct ? .05 : Math.max(Math.abs(hi) * .1, 1));
      lo -= pad; hi += pad;
      if (Math.min(...all) >= 0) lo = Math.max(0, lo);
      const W = el.clientWidth || 600, H = 170, L = 46, B = 18, T = 6, R = 8;
      const X = s => L + s / secs * (W - L - R), Y = v => T + (H - T - B) * (1 - (v - lo) / (hi - lo || 1));
      const path = a => { let d = '', pen = false; a.forEach((v, s) => { if (!isFinite(v)) { pen = false; return; } d += `${pen ? 'L' : 'M'}${X(s).toFixed(1)},${Y(v).toFixed(1)}`; pen = true; }); return d; };
      let svg = '';
      for (let i = 0; i <= 3; i++) {
        const v = lo + (hi - lo) * i / 3, y = Y(v);
        svg += `<line x1="${L}" x2="${W - R}" y1="${y}" y2="${y}" class="g"/><text x="${L - 4}" y="${y + 3}" text-anchor="end" fill="${m.color}">${m.pct ? Math.round(v * 100) + '%' : compact(v)}</text>`;
      }
      const ticks = Math.max(1, Math.floor((W - L - R) / 70));
      for (let i = 0; i <= ticks; i++) { const s = Math.round(secs * i / ticks); svg += `<text x="${X(s)}" y="${H - 4}" text-anchor="middle" fill="currentColor" opacity=".6">${clock(s)}</text>`; }
      // green where this run is above the average, red where it is below
      let fills = '';
      for (let s = 0; s < secs; s++) {
        const a0 = mine[s], a1 = mine[s + 1], b0 = avg[s], b1 = avg[s + 1];
        if (![a0, a1, b0, b1].every(isFinite)) continue;
        const d0 = a0 - b0, d1 = a1 - b1, quad = (c, xa, xb, ya0, ya1, yb0, yb1) => `<path d="M${xa},${ya0}L${xb},${ya1}L${xb},${yb1}L${xa},${yb0}Z" fill="${c}" opacity=".22"/>`;
        if (d0 >= 0 && d1 >= 0) fills += quad(COLOR.green, X(s), X(s + 1), Y(a0), Y(a1), Y(b0), Y(b1));
        else if (d0 <= 0 && d1 <= 0) fills += quad(COLOR.red, X(s), X(s + 1), Y(a0), Y(a1), Y(b0), Y(b1));
        else {
          const f = d0 / (d0 - d1), xc = X(s + f), yc = Y(a0 + (a1 - a0) * f);
          fills += `<path d="M${X(s)},${Y(a0)}L${xc},${yc}L${X(s)},${Y(b0)}Z" fill="${d0 > 0 ? COLOR.green : COLOR.red}" opacity=".22"/><path d="M${X(s + 1)},${Y(a1)}L${xc},${yc}L${X(s + 1)},${Y(b1)}Z" fill="${d1 > 0 ? COLOR.green : COLOR.red}" opacity=".22"/>`;
        }
      }
      svg += fills;
      svg += `<path d="${path(avg)}" fill="none" stroke="${m.color}" stroke-width="1.5" stroke-dasharray="4 3" opacity=".7"/>`;
      if (best) svg += `<path d="${path(best)}" fill="none" stroke="${m.color}" stroke-width="1.25" stroke-dasharray="1 3" stroke-linecap="round" opacity=".9"/>`;
      svg += `<path d="${path(mine)}" fill="none" stroke="${m.color}" stroke-width="2" stroke-linejoin="round"/>`;
      const last = mine.map((v, s) => isFinite(v) ? s : -1).filter(s => s >= 0).pop();
      if (last != null) svg += `<circle cx="${X(last)}" cy="${Y(mine[last])}" r="3.5" fill="${m.color}"/>`;
      svg += '<g class="hv" visibility="hidden"><line y1="0" y2="' + H + '" stroke="currentColor" opacity=".4"/><circle r="4" fill="' + m.color + '" stroke="#fff" stroke-width="1.5"/></g>';

      const endOf = a => { const f = finite(a); return f.length ? f[f.length - 1] : NaN; };
      const delta = (a, b, name) => {
        const d = a - b;
        if (!isFinite(d)) return '';
        const text = m.pct ? `${Math.abs(d * 100).toFixed(1)} pt` : `${num(Math.abs(d), 1)}${b > 0 ? ` (${(Math.abs(d) / b * 100).toFixed(1)}%)` : ''}`;
        return `<span style="color:${d >= 0 ? COLOR.green : COLOR.red};font-weight:600">${name}${d >= 0 ? '+' : '−'}${text}</span>`;
      };
      const head = `<div class="ctitle">${T('Compare')}<span class="chips2">${CMP.map(c => `<button class="chip${c.key === metric ? '' : ' off'}" data-m="${c.key}"><i style="background:${c.color}"></i>${T(c.name)}</button>`).join('')}</span></div>`
        + `<div class="deltas">${delta(endOf(mine), endOf(avg), '')}${best ? `<span class="dim">${T('Best')} ${delta(endOf(mine), endOf(best), '').replace(/<[^>]+>/g, '')}</span>` : ''}`
        + `<span class="legend"><span><u></u>${T('This run')}</span><span><u class="dash"></u>${T('Average')}</span>${best ? `<span><u class="dot"></u>${T('Best')}</span>` : ''}</span></div>`;
      el.innerHTML = head + `<div class="cwrap"><svg class="cchart" viewBox="0 0 ${W} ${H}" height="${H}">${svg}</svg><div class="tip" hidden></div></div>`;
      el.querySelectorAll('[data-m]').forEach(b => b.addEventListener('click', () => { metric = b.dataset.m; render(); }));

      const cv = el.querySelector('.cchart'), tip = el.querySelector('.tip'), hv = cv.querySelector('.hv');
      cv.addEventListener('pointermove', e => {
        const r = cv.getBoundingClientRect(), x = (e.clientX - r.left) / r.width * W;
        const s = Math.min(secs, Math.max(0, Math.round((x - L) / (W - L - R) * secs)));
        if (!isFinite(mine[s])) { hv.setAttribute('visibility', 'hidden'); tip.hidden = true; return; }
        hv.setAttribute('visibility', 'visible');
        hv.firstChild.setAttribute('x1', X(s)); hv.firstChild.setAttribute('x2', X(s));
        hv.lastChild.setAttribute('cx', X(s)); hv.lastChild.setAttribute('cy', Y(mine[s]));
        const d = mine[s] - avg[s];
        tip.innerHTML = `<b>${clock(s)}</b><br>${T('This run')} ${fmt(mine[s], true)} / ${T('Average')} ${fmt(avg[s], true)}${best && isFinite(best[s]) ? ` / ${T('Best')} ${fmt(best[s], true)}` : ''}<br><span style="color:${d >= 0 ? COLOR.green : COLOR.red}">${d >= 0 ? '+' : '−'}${m.pct ? Math.abs(d * 100).toFixed(1) + ' pt' : num(Math.abs(d), 1)}</span>`;
        tip.hidden = false;
        const px = X(s) / W * r.width;
        tip.style.left = (px > r.width / 2 ? px - tip.offsetWidth - 10 : px + 10) + 'px';
      });
      cv.addEventListener('pointerleave', () => { hv.setAttribute('visibility', 'hidden'); tip.hidden = true; });
      show(false);
    }

    return {
      // entries: this run first, then the others; each { run: running totals, score, best? }
      set(entries) { runs = entries; render(); },
      clear() { runs = null; show(true); },
      redraw: render,
    };
  }

  // ---- overview of the scenario: totals, advice, score history, score by sensitivity and mouse speed over all runs ----

  const niceStep = raw => { const p = 10 ** Math.floor(Math.log10(raw)); return [1, 2, 2.5, 5, 10].map(m => m * p).find(x => raw <= x) || 10 * p; };
  const niceTicks = (lo, hi, n = 4) => {
    if (!(hi > lo)) return [lo];
    const step = niceStep((hi - lo) / n), out = [];
    for (let v = Math.ceil(lo / step) * step; v <= hi + step * 1e-9; v += step) out.push(+v.toFixed(10));
    return out;
  };
  const esc = s => s.replace(/[&<>]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
  const day = t => new Date(t * 1000).toLocaleDateString([], { dateStyle: 'medium' });

  // The file name of the scenario's overview: the first 16 hex digits of the SHA-1 of its lower-case name (the app uses the same rule).
  async function overviewName(scenario) {
    const d = new Uint8Array(await crypto.subtle.digest('SHA-1', new TextEncoder().encode(scenario.toLowerCase())));
    return 'scenario-' + [...d].map(b => b.toString(16).padStart(2, '0')).join('').slice(0, 16) + '.cache';
  }

  function tiles(ov) {
    const t = [['Time', ov.time > 0 ? span(ov.time) : dash], ['Runs', String(ov.plays)], ['Best', ov.best ? num(ov.best, 1) : dash]];
    if (ov.accuracy != null || ov.ttk != null) {
      if (ov.pbs > 0) t.push(['PB', String(ov.pbs), COLOR.green]);
      t.push(['Accuracy', pct(ov.accuracy)], ['TTK', ov.ttk == null ? dash : ov.ttk.toFixed(2) + 's']);
    }
    return `<div class="tiles">${t.map(([k, v, c]) => `<div class="tile"><small>${T(k)}</small><b${c ? ` style="color:${c}"` : ''}>${v}</b></div>`).join('')}</div>`;
  }

  // Advice: the app's format string and figures, worded here in the popup's language, with **x** as bold.
  const adviceCard = ov => !ov.advice || !ov.advice.length ? '' :
    `<div class="card2"><div class="ctitle">${T('Advice')}</div>${ov.advice.map(a => `<p class="adv">${esc(F(a.f, ...a.a)).replace(/\*\*(.+?)\*\*/g, '<b>$1</b>')}</p>`).join('')}</div>`;

  const LINES = [
    { key: 'acc', name: 'Accuracy', color: COLOR.green, fmt: v => pct(v, 1) },
    { key: 'kills', name: 'Kills', color: COLOR.orange, fmt: v => num(v, 0) },
    { key: 'ttk', name: 'TTK', color: COLOR.purple, fmt: v => v.toFixed(2) + 's' },
    { key: 'over', name: 'Overshots', color: COLOR.red, fmt: v => num(v, 0) },
    { key: 'reloads', name: 'Reloads', color: '#66d4cf', fmt: v => num(v, 0) },
    { key: 'dist', name: 'Distance moved', color: COLOR.accent, fmt: v => num(v, 2) },
    { key: 'speed', name: 'Average speed', color: COLOR.yellow, fmt: v => v.toFixed(1) + ' cm/s' },
    { key: 'reaction', name: 'Reaction time', color: COLOR.teal, fmt: v => num(v, 0) + ' ms' },
  ];

  // The score of every run in order, a dashed moving average, and any of the other figures on their own scales; hover for the details, click a point to open that run.
  function renderHistory(el, ov, open) {
    const R = ov.records, n = R.score.length;
    if (!n) { el.hidden = true; return; }
    const MAX = 70;
    let start = Math.max(0, n - MAX);
    const shown = new Set();
    const lines = LINES.filter(l => R[l.key].some(v => v != null && (l.key === 'reloads' || l.key === 'dist' ? v > .01 : true)));

    function draw() {
      const W = el.clientWidth || 600, H = 210, L = 44, T = 8, B = 20, rightCols = lines.filter(l => shown.has(l.key)).length;
      const Rm = 8 + rightCols * 38, end = Math.min(n, start + MAX), m = end - start;
      const X = i => L + (m <= 1 ? 0 : (i - start) / (m - 1)) * (W - L - Rm), idx = Array.from({ length: m }, (_, k) => start + k);
      const sc = R.score.slice(start, end);
      let lo = Math.min(...sc), hi = Math.max(...sc);
      const pad = (hi - lo) * .08 || 1; lo = Math.max(0, lo - pad); hi += pad;
      const Y = v => T + (H - T - B) * (1 - (v - lo) / (hi - lo));
      let svg = '';
      for (const v of niceTicks(lo, hi)) svg += `<line x1="${L}" x2="${W - Rm}" y1="${Y(v)}" y2="${Y(v)}" class="g"/><text x="${L - 4}" y="${Y(v) + 3}" text-anchor="end" fill="currentColor" opacity=".6">${num(v, 0)}</text>`;
      const avg = idx.map(i => { const a = R.score.slice(Math.max(0, i - 4), i + 1); return a.reduce((x, y) => x + y, 0) / a.length; });
      const area = curve(idx.map(i => [X(i), Y(R.score[i])]));
      svg += `<path d="${area}L${X(end - 1)},${H - B}L${X(start)},${H - B}Z" fill="${COLOR.accent}" opacity=".14"/>`;
      svg += `<path d="${curve(idx.map((i, k) => [X(i), Y(avg[k])]))}" fill="none" stroke="currentColor" stroke-width="1.2" stroke-dasharray="4 4" opacity=".45"/>`;
      svg += `<path d="${area}" fill="none" stroke="${COLOR.accent}" stroke-width="2" stroke-linejoin="round"/><circle cx="${X(end - 1)}" cy="${Y(R.score[end - 1])}" r="3.5" fill="${COLOR.accent}"/>`;
      let col = 0;
      const extra = [];
      for (const l of lines.filter(l => shown.has(l.key))) {
        const pts = idx.filter(i => R[l.key][i] != null), vals = pts.map(i => R[l.key][i]);
        if (!vals.length) continue;
        let a = Math.min(...vals), b = Math.max(...vals);
        if (l.key === 'acc') { a = Math.max(0, Math.floor((a - .02) * 20) / 20); b = Math.min(1, Math.ceil((b + .02) * 20) / 20); } else { const p = (b - a) * .08 || 1; a = a >= 0 ? Math.max(0, a - p) : a - p; b += p; }
        const Yl = v => T + (H - T - B) * (1 - (v - a) / (b - a || 1));
        let d = '', pen = false, last = null;
        for (const i of idx) {
          const v = R[l.key][i];
          if (v == null) { pen = false; continue; }
          d += `${pen ? 'L' : (last != null ? 'M' : 'M')}${X(i)},${Yl(v)}`; pen = true; last = i;
        }
        svg += `<path d="${d}" fill="none" stroke="${l.color}" stroke-width="1.5" stroke-linejoin="round"/>`;
        for (const t of niceTicks(a, b, 3)) svg += `<text x="${W - 4 - col * 38}" y="${Yl(t) + 3}" text-anchor="end" fill="${l.color}">${l.key === 'acc' ? Math.round(t * 100) + '%' : compact(t)}</text>`;
        extra.push(l); col++;
      }
      svg += `<g class="hv" visibility="hidden"><line y1="${T}" y2="${H - B}" stroke="currentColor" opacity=".4"/><circle r="4.5" fill="${COLOR.accent}" stroke="#fff" stroke-width="1.5"/></g>`;
      const chips = lines.map(l => `<button class="chip${shown.has(l.key) ? '' : ' off'}" data-l="${l.key}"><i style="background:${l.color}"></i>${T(l.name)}</button>`).join('');
      el.innerHTML = `<div class="ctitle">${T('Run stats')}<span class="chips2">${chips}</span></div><div class="cwrap"><svg class="cchart" viewBox="0 0 ${W} ${H}" height="${H}">${svg}</svg><div class="tip" hidden></div></div>`
        + (n > MAX ? `<input type="range" class="range" min="0" max="${n - MAX}" value="${start}">` : '');
      el.hidden = false;
      el.querySelectorAll('[data-l]').forEach(b => b.addEventListener('click', () => { if (shown.has(b.dataset.l)) shown.delete(b.dataset.l); else shown.add(b.dataset.l); draw(); }));
      const rg = el.querySelector('.range');
      if (rg) rg.addEventListener('input', () => { start = +rg.value; draw(); });
      const cv = el.querySelector('.cchart'), tip = el.querySelector('.tip'), hv = cv.querySelector('.hv');
      const nearest = e => { const r = cv.getBoundingClientRect(), x = (e.clientX - r.left) / r.width * W; return Math.min(end - 1, Math.max(start, start + Math.round((x - L) / ((W - L - Rm) / Math.max(1, m - 1))))); };
      cv.addEventListener('pointermove', e => {
        const i = nearest(e), r = cv.getBoundingClientRect();
        hv.setAttribute('visibility', 'visible');
        hv.firstChild.setAttribute('x1', X(i)); hv.firstChild.setAttribute('x2', X(i)); hv.lastChild.setAttribute('cx', X(i)); hv.lastChild.setAttribute('cy', Y(R.score[i]));
        tip.innerHTML = `<b>${day(R.t[i])}</b><br>${T('Score')} ${num(R.score[i], 1)}` + extra.filter(l => R[l.key][i] != null).map(l => `<br><span style="color:${l.color}">●</span> ${T(l.name)} ${l.fmt(R[l.key][i])}`).join('');
        tip.hidden = false;
        const px = X(i) / W * r.width;
        tip.style.left = (px > r.width / 2 ? px - tip.offsetWidth - 10 : px + 10) + 'px';
      });
      cv.addEventListener('pointerleave', () => { hv.setAttribute('visibility', 'hidden'); tip.hidden = true; });
      cv.addEventListener('click', e => open(R.t[nearest(e)]));
    }
    draw();
    return draw;
  }

  const SENS_COLORS = ['#4C9BFF', '#FF9F43', '#3DD68C', '#E05CC9', '#F5D04A'];

  // Score against sensitivity: a dot per run, and per sensitivity its runs, average and best with overlapping bars.
  function renderSens(el, ov) {
    const S = ov.sens;
    if (!S || !S.groups.length) { el.hidden = true; return; }
    const W = el.clientWidth || 600, H = 170, L = 44, B = 20, T = 8, Rm = 10;
    const cms = S.points.map(p => p[1]), scores = S.points.map(p => p[2]);
    let x0 = Math.min(...cms), x1 = Math.max(...cms), y0 = Math.min(...scores), y1 = Math.max(...scores);
    const px = (x1 - x0) * .08 || 1, py = (y1 - y0) * .1 || 1; x0 -= px; x1 += px; y0 = Math.max(0, y0 - py); y1 += py;
    const X = v => L + (v - x0) / (x1 - x0) * (W - L - Rm), Y = v => T + (H - T - B) * (1 - (v - y0) / (y1 - y0));
    let svg = '';
    for (const v of niceTicks(y0, y1)) svg += `<line x1="${L}" x2="${W - Rm}" y1="${Y(v)}" y2="${Y(v)}" class="g"/><text x="${L - 4}" y="${Y(v) + 3}" text-anchor="end" fill="currentColor" opacity=".6">${num(v, 0)}</text>`;
    for (const v of niceTicks(x0, x1, 5)) svg += `<text x="${X(v)}" y="${H - 5}" text-anchor="middle" fill="currentColor" opacity=".6">${num(v, 1)}</text>`;
    const topOf = S.groups.map((_, g) => Math.max(...S.points.filter(p => p[0] === g).map(p => p[2])));
    S.points.forEach(p => { svg += `<circle cx="${X(p[1])}" cy="${Y(p[2])}" r="${p[2] === topOf[p[0]] ? 5 : 3}" fill="${SENS_COLORS[p[0]]}" opacity="${p[2] === topOf[p[0]] ? 1 : .75}"><title>${num(p[1], 2)} cm/360 · ${day(p[3])}</title></circle>`; });
    const hi = Math.max(...S.groups.map(g => g.best)), lo0 = Math.min(...S.groups.map(g => g.avg));
    const floor = hi - lo0 < .01 * hi ? 0 : Math.max(0, (lo0 - .05 * hi) / .95), frac = v => Math.max(0, Math.min(1, (v - floor) / (hi - floor || 1)));
    const rows = S.groups.map((g, i) => `<div class="sens"><div class="srow"><i style="background:${SENS_COLORS[i]}"></i><span>${num(g.cm, 2)} cm/360</span><span class="dim">${F('{0} runs', g.runs)}</span><span class="sv2"><span class="dim">${num(g.avg, 1)}</span> / <b style="color:${SENS_COLORS[i]}">${num(g.best, 1)}</b></span></div>`
      + `<div class="obar"><i style="width:${frac(g.best) * 100}%;background:${SENS_COLORS[i]}"></i>${num(g.avg, 1) === num(g.best, 1) ? '' : `<i class="avg" style="width:${frac(g.avg) * 100}%;background:${SENS_COLORS[i]}"></i>`}</div></div>`).join('');
    el.innerHTML = `<div class="ctitle">${T('Score by sensitivity')}<span class="legend"><span>${T('Average / Best')}</span></span></div><svg class="cchart" viewBox="0 0 ${W} ${H}" height="${H}">${svg}</svg><div class="sensrows">${rows}</div>`;
    el.hidden = false;
  }

  // The mouse-speed card of the overview: the top 5% of runs by score, all runs and the bottom 5%.
  function renderSpeedAll(el, scenario, rows) {
    const withSpeed = rows.filter(r => r[11]).sort((a, b) => b[RUN.score] - a[RUN.score]);
    if (!withSpeed.length) { el.hidden = true; return; }
    const edge = Math.max(1, Math.ceil(withSpeed.length * .05));
    const mean0 = group => spec => {
      const vals = group.map(r => speedValue(r[11], spec, r[RUN.cm])).filter(v => v != null);
      return vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : null;
    };
    speedCard(el, scenario, withSpeed, [
      { name: 'Top 5%', color: SPEED_COLOR.top, get: mean0(withSpeed.slice(0, edge)) },
      { name: 'Average', color: SPEED_COLOR.avg, get: mean0(withSpeed) },
      { name: 'Bottom 5%', color: SPEED_COLOR.bottom, get: mean0(withSpeed.slice(-edge)) },
    ]);
  }

  // ---- popup over the page; a shadow root keeps the site's styles out ------------------------------------------

  let host = null;
  let cleanup = () => {};

  function close() {
    cleanup();
    if (host) { host.remove(); host = null; document.removeEventListener('keydown', onKey, true); removeEventListener('resize', onResize); }
  }
  let toggleTrail = () => {};
  function onKey(e) {
    if (e.key === 'Escape') { e.stopPropagation(); close(); }
    else if ((e.key === 't' || e.key === 'T') && !e.ctrlKey && !e.metaKey && !e.altKey) { e.stopPropagation(); toggleTrail(); }
  }
  let onResize = () => {};

  // The popup takes the page's own background, text color and font, so it looks like part of the site in light and dark mode alike.
  const pixel = (() => {
    const ctx = document.createElement('canvas').getContext('2d', { willReadFrequently: true });
    return css => { // any CSS color -> [r, g, b, a] (a 0..255)
      ctx.clearRect(0, 0, 1, 1);
      ctx.fillStyle = '#000';
      ctx.fillStyle = css;
      ctx.fillRect(0, 0, 1, 1);
      return ctx.getImageData(0, 0, 1, 1).data;
    };
  })();
  const luminance = ([r, g, b]) => [r, g, b].map(v => { v /= 255; return v <= .03928 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4; }).reduce((s, v, i) => s + v * [.2126, .7152, .0722][i], 0);
  const contrast = (a, b) => { const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x); return (hi + .05) / (lo + .05); };

  // The color the page really shows: the site may paint its theme on a wrapper inside <body> (or follow its own switch instead of the browser's), so ask which
  // elements are drawn at the edge of the window, topmost first, and take the first opaque background; its text color is the one the page text inherits there.
  function siteTheme() {
    let bg = null, source = document.body;
    const stack = [];
    for (const [x, y] of [[2, innerHeight / 2], [innerWidth - 2, innerHeight / 2], [innerWidth / 2, innerHeight - 2]]) stack.push(...document.elementsFromPoint(x, y));
    for (const e of [...stack, document.body, document.documentElement]) {
      const p = pixel(getComputedStyle(e).backgroundColor);
      if (p[3] === 255) { bg = p; source = e; break; } // a transparent layer shows what is behind it, so look further down
    }
    if (!bg) {
      const scheme = getComputedStyle(document.documentElement).colorScheme;
      const dark = scheme === 'dark' || scheme !== 'light' && matchMedia('(prefers-color-scheme: dark)').matches; // "light dark" and "normal" follow the browser
      bg = dark ? [22, 24, 29] : [255, 255, 255];
    }
    let fg = pixel(getComputedStyle(source).color);
    if (fg[3] !== 255 || contrast(fg, bg) < 3) fg = luminance(bg) > .4 ? [17, 17, 17] : [240, 240, 240];
    const rgb = c => `rgb(${c[0]},${c[1]},${c[2]})`;
    return { bg: rgb(bg), fg: rgb(fg), font: getComputedStyle(document.body).fontFamily || 'system-ui,sans-serif' };
  }

  function open(replays) {
    close();
    host = document.createElement('div');
    host.style.cssText = 'position:fixed;inset:0;z-index:2147483647';
    const theme = siteTheme();
    host.style.setProperty('--bg', theme.bg);
    host.style.setProperty('--fg', theme.fg);
    host.style.setProperty('--font', theme.font);
    const root = host.attachShadow({ mode: 'closed' });
    root.innerHTML = `
      <style>
        .scrim{position:absolute;inset:0;background:rgba(0,0,0,.65);display:flex;align-items:center;justify-content:center;padding:16px;box-sizing:border-box;font:13px var(--font)}
        .card{max-height:100%;overflow-y:auto;scrollbar-width:thin;--dim:color-mix(in srgb,var(--fg) 60%,var(--bg));--hover:color-mix(in srgb,var(--fg) 10%,var(--bg));--on:color-mix(in srgb,#0a84ff 24%,var(--bg));width:min(1600px,100%);background:var(--bg);color:var(--fg);border:1px solid color-mix(in srgb,var(--fg) 14%,var(--bg));border-radius:12px;padding:8px 10px 10px;box-shadow:0 20px 60px #0009}
        .bar{display:flex;gap:8px;align-items:center;padding-bottom:6px}
        .bar b{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:13px}
        .bar span{color:var(--dim);font-size:12px}
        button{display:inline-flex;align-items:center;background:none;color:var(--dim);border:0;border-radius:6px;padding:2px 6px;font:inherit;line-height:20px;cursor:pointer}
        button:hover{background:var(--hover);color:var(--fg)}
        .body{display:grid;grid-template-columns:minmax(0,1fr) 200px;gap:12px;align-items:start}
        .list{position:sticky;top:0}
        .main{min-width:0}
        .panel{padding-top:10px}
        .panel[hidden]{display:none}
        .chips{display:flex;flex-wrap:wrap;gap:6px;padding-bottom:6px}
        .chip{gap:6px;padding:2px 8px;border:1px solid color-mix(in srgb,var(--fg) 16%,var(--bg));color:var(--fg);font-size:12px}
        .chip.off{opacity:.45}
        .chip i{width:8px;height:8px;border-radius:50%}
        .chip b{font-weight:600;font-variant-numeric:tabular-nums;min-width:2.2em;text-align:right}
        .chart{display:block;width:100%;height:150px;cursor:ew-resize;touch-action:none;user-select:none}
        .chart .g{stroke:color-mix(in srgb,var(--fg) 12%,var(--bg));stroke-width:1}
        .chart text{font-size:10px}
        .chiprow{display:flex;gap:8px;align-items:center;justify-content:space-between}
        .chiprow .chips{flex:1}
        .trailbtn{border:1px solid color-mix(in srgb,var(--fg) 16%,var(--bg));color:var(--fg);font-size:12px;padding:2px 10px}
        .trailbtn.off{opacity:.45}
        .trail{position:absolute;pointer-events:none}
        .card2{margin-top:14px;padding-top:10px;border-top:1px solid color-mix(in srgb,var(--fg) 12%,var(--bg))}
        .card2[hidden]{display:none}
        .ctitle{display:flex;align-items:center;gap:10px;flex-wrap:wrap;font-weight:600;margin-bottom:6px}
        .chips2{display:flex;gap:4px;flex-wrap:wrap;font-weight:400}
        .legend{margin-left:auto;display:flex;gap:12px;font-weight:400;font-size:12px;color:var(--dim)}
        .legend i{display:inline-block;width:8px;height:8px;border-radius:50%;margin-right:4px}
        .legend u{display:inline-block;width:16px;border-top:2px solid var(--fg);margin-right:4px;vertical-align:middle}
        .legend u.dash{border-top-style:dashed}
        .legend u.dot{border-top-style:dotted}
        .deltas{display:flex;gap:14px;align-items:center;margin-bottom:4px}
        .deltas .dim{color:var(--dim)}
        .cwrap{position:relative}
        .cchart{display:block;width:100%}
        .cchart .g{stroke:color-mix(in srgb,var(--fg) 12%,var(--bg));stroke-width:1}
        .cchart text{font-size:10px}
        .tip{position:absolute;top:4px;background:var(--bg);border:1px solid color-mix(in srgb,var(--fg) 20%,var(--bg));border-radius:8px;padding:6px 8px;font-size:12px;pointer-events:none;box-shadow:0 4px 14px #0006;white-space:nowrap;z-index:1}
        .none{color:var(--dim)}
        .sg{font-weight:600;margin:10px 0 3px}
        .scols{display:grid;grid-template-columns:1fr 1fr;gap:0 28px;align-items:start}
        .scols>div{min-width:0}
        @media (max-width:900px){.scols{grid-template-columns:1fr}}
        .sgrid{display:grid;grid-template-columns:112px 1fr;gap:0 10px;align-items:center;--line:color-mix(in srgb,var(--fg) 12%,var(--bg))}
        .sl{color:var(--dim);border-bottom:1px solid var(--line);min-height:30px;display:flex;align-items:center;font-size:12px}
        .sv{display:flex;align-items:center;gap:10px;height:30px;border-bottom:1px solid var(--line)}
        .trk{flex:1;min-width:60px;display:flex;flex-direction:column;gap:2px;background:color-mix(in srgb,var(--fg) 8%,var(--bg))}
        .trk i{display:block;height:5px;border-radius:0 3px 3px 0}
        .nums{display:grid}
        .nums b{text-align:right;font-size:12px;font-weight:600;font-variant-numeric:tabular-nums;white-space:nowrap}
        .statbtn{padding:2px 6px;font-weight:600;color:var(--fg)}
        .statbtn::before{content:'\\25B8';display:inline-block;width:14px;transition:transform .15s}
        .statbtn.open::before{transform:rotate(90deg)}
        .stats[hidden]{display:none}
        .pb{margin-left:6px;font-size:10px;font-weight:700;letter-spacing:.03em;color:#74a98a;border:1px solid currentColor;border-radius:4px;padding:0 4px;line-height:14px}
        .stage[hidden]{display:none}
        .overview[hidden]{display:none}
        .tiles{display:grid;grid-template-columns:repeat(auto-fill,minmax(124px,1fr));gap:8px}
        .tile{background:var(--hover);border-radius:10px;padding:10px 12px}
        .tile small{display:block;color:var(--dim);font-size:11.5px;margin-bottom:2px}
        .tile b{font-size:22px;font-weight:600}
        .adv{margin:4px 0;color:var(--dim);font-size:13px;line-height:1.5}
        .adv b{color:var(--fg)}
        .range{width:100%;margin-top:6px}
        .sensrows{margin-top:8px;display:grid;gap:8px}
        .srow{display:flex;align-items:center;gap:10px}
        .srow i{width:8px;height:8px;border-radius:50%}
        .srow .sv2{margin-left:auto;font-variant-numeric:tabular-nums}
        .dim{color:var(--dim)}
        .obar{position:relative;height:8px;border-radius:4px;background:color-mix(in srgb,var(--fg) 8%,var(--bg));margin-top:4px;overflow:hidden}
        .obar i{position:absolute;left:0;top:0;bottom:0;border-radius:4px}
        .obar i.avg{background:#fff!important;opacity:.45}
        .row.first{border-bottom:1px solid color-mix(in srgb,var(--fg) 12%,var(--bg));margin-bottom:2px}
        .tables{display:grid;grid-template-columns:1fr 1fr;padding-top:12px;--line:color-mix(in srgb,var(--fg) 12%,var(--bg))}
        .tab{display:grid;grid-template-columns:1fr auto auto;align-content:start}
        .tab:first-child{padding-right:14px}
        .tab:last-child{padding-left:14px;border-left:1px solid var(--line)}
        .tab>div{padding:3px 0 3px 14px;border-bottom:1px solid var(--line)}
        .tab>.k{padding-left:0}
        .tab .k{color:var(--dim)}
        .tab .h{color:var(--dim);font-size:11px;text-align:right}
        .tab .v{font-weight:600;text-align:right;font-variant-numeric:tabular-nums}
        .tab .a{color:var(--dim);text-align:right;font-variant-numeric:tabular-nums;min-width:4.5em}
        .stage{position:relative;display:flex;justify-content:center}
        video{display:block;width:100%;aspect-ratio:16/9;background:#000;border-radius:8px}
        .note{position:absolute;inset:0;display:flex;flex-direction:column;gap:12px;align-items:center;justify-content:center;text-align:center;white-space:pre-line;overflow-wrap:anywhere;padding:24px;color:#aeb3c2;background:rgba(0,0,0,.5);border-radius:8px}
        .note button{background:#2b5fb4;color:#fff;padding:4px 14px}
        .note button:hover{background:#3470d0;color:#fff}
        .note[hidden]{display:none}
        .note.err{color:#ff9a9a}
        .list{overflow-y:auto;scrollbar-width:thin}
        .row{display:grid;grid-template-columns:1fr auto;gap:12px;padding:4px 8px;cursor:pointer;white-space:nowrap}
        .row:hover{background:var(--hover)}
        .row.on{background:var(--on)}
        .d{color:var(--dim)}
        .n{text-align:right;font-variant-numeric:tabular-nums}
      </style>
      <div class="scrim"><div class="card">
        <div class="bar"><b></b><span></span><button class="app" title="${T('Continue in the app')}"></button><button class="x">✕</button></div>
        <div class="body">
          <div class="main">
            <div class="stage"><video controls></video><canvas class="trail"></canvas><div class="note" hidden></div></div>
            <div class="panel" hidden><div class="chiprow"><button class="statbtn">${T('Statistics')}</button><button class="trailbtn" title="${T('Show or hide the trail (T)')}" hidden>${T('Trail')}</button></div><div class="stats" hidden><div class="chips"></div><svg class="chart"></svg><div class="tables"></div><div class="card2 cmp" hidden></div><div class="card2 spd" hidden></div></div></div>
            <div class="overview" hidden></div>
          </div>
          <div class="list"></div>
        </div>
      </div></div>`;
    const $ = s => root.querySelector(s);
    $('b').textContent = replays[0].scenario;
    $('.bar span').textContent = F(replays.length === 1 ? '{0} replay' : '{0} replays', replays.length);
    $('.app').innerHTML = OPEN_ICON;
    const list = $('.list'), video = $('video'), stage = $('.stage'), note = $('.note'), panel = $('.panel');
    const chart = makeChart($('.chips'), $('.chart'), video);
    const trail = makeTrail($('.trail'), video);
    const compare = makeCompare($('.cmp'));
    const ovEl = $('.overview');
    // The statistics under the chart start folded; the choice stays while the user goes from one replay to the next.
    const statBtn = $('.statbtn'), stats = $('.stats');
    statBtn.addEventListener('click', () => {
      stats.hidden = !stats.hidden;
      statBtn.classList.toggle('open', !stats.hidden);
      if (!stats.hidden) { chart.draw(); compare.redraw(); } // they could not be measured while hidden
    });
    let mode = 'run', drawHistory = () => {};
    const trailBtn = $('.trailbtn');
    const syncTrailBtn = () => trailBtn.classList.toggle('off', !trail.on);
    trailBtn.addEventListener('click', () => { trail.toggle(); syncTrailBtn(); });
    toggleTrail = () => { if (!trailBtn.hidden) { trail.toggle(); syncTrailBtn(); } };
    // The overlay covers exactly the video element, wherever fit() put it.
    const placeTrail = () => { const c = $('.trail'); c.style.left = video.offsetLeft + 'px'; c.style.top = video.offsetTop + 'px'; c.style.width = video.clientWidth + 'px'; c.style.height = video.clientHeight + 'px'; trail.draw(); };

    // The card is as tall as the video: the video takes the width of its column unless the window is too short for that, and the list never grows past it.
    function fit() {
      if (!video.videoWidth) return;
      const ratio = video.videoWidth / video.videoHeight;
      const room = innerHeight - 32 - 48; // scrim padding, header and card padding
      video.style.aspectRatio = String(ratio);
      video.style.width = Math.min(stage.clientWidth, Math.max(240, room * ratio)) + 'px';
      list.style.maxHeight = video.clientHeight + 'px';
      placeTrail();
    }
    video.addEventListener('loadedmetadata', () => { fit(); chart.draw(); });
    for (const ev of ['timeupdate', 'seeked', 'seeking']) video.addEventListener(ev, () => { chart.update(); trail.draw(); });
    let frame = 0;
    const follow = () => { chart.update(); trail.draw(); frame = requestAnimationFrame(follow); }; // the playhead moves smoothly while the video plays
    video.addEventListener('play', () => { cancelAnimationFrame(frame); follow(); });
    video.addEventListener('pause', () => cancelAnimationFrame(frame));
    onResize = () => { fit(); chart.draw(); compare.redraw(); drawHistory(); };
    addEventListener('resize', onResize);

    // The clip is a file in the folder the user picked; the popup plays it straight from there, so it starts without reading the whole file first.
    let current = 0, seq = 0, blobUrl = null;
    function release() {
      seq++;
      cancelAnimationFrame(frame);
      if (blobUrl) { URL.revokeObjectURL(blobUrl); blobUrl = null; }
    }
    cleanup = release;
    function showNote(text, error, action) {
      note.textContent = text;
      note.className = 'note' + (error ? ' err' : '');
      if (action) {
        const b = document.createElement('button');
        b.textContent = action.label;
        b.addEventListener('click', action.run);
        note.append(b);
      }
      note.hidden = false;
    }

    // Asks for the folder; a click on the button is what lets the browser show its folder dialog.
    function askFolder(text, error) {
      showNote(`${text}\n${videoDir}`, error, {
        label: T('Select folder'),
        run: async () => {
          try { await pickFolder(); } catch { return; } // the dialog was closed
          play(current);
        },
      });
    }

    // The statistics file of a replay (written by the app next to the videos); a replay without one just has no panel.
    async function loadPanel(folder, i, mine) {
      try {
        const st = await cachedStats(folder, replays[i]);
        if (mine !== seq) return;
        const hasChart = chart.show(st);
        $('.chips').hidden = !hasChart;
        $('.chart').style.display = hasChart ? 'block' : 'none';
        const rows = runsBy[replays[i].scenario.toLowerCase()] || [];
        renderTable($('.tables'), replays[i], st, rows);
        renderSpeed($('.spd'), replays[i], st, rows);
        trailBtn.hidden = !trail.show(st);
        syncTrailBtn();
        panel.hidden = false;
        fit();
        loadCompare(folder, i, mine, st);
      } catch { if (mine === seq) panel.hidden = true; }
    }

    // The other replays of the scenario, read in the background: the compare chart needs the per-second data of all of them.
    async function loadCompare(folder, i, mine, st) {
      const own = runningOf(st);
      if (!own) { compare.clear(); return; }
      const entries = [{ run: own, score: replays[i].score }];
      const others = await Promise.all(replays.filter((_, k) => k !== i).map(async r => {
        try { const run = runningOf(await cachedStats(folder, r)); return run && { run, score: r.score }; } catch { return null; }
      }));
      if (mine !== seq) return;
      entries.push(...others.filter(Boolean));
      const top = entries.reduce((b, e) => e.score > b.score ? e : b, entries[0]);
      if (top !== entries[0]) top.best = true;
      compare.set(entries);
    }

    const ovCache = new Map(); // scenario -> its overview file
    // The overview replaces the video and the run panel: the totals, advice, score history, sensitivity and speed of every run of the scenario.
    async function showOverview() {
      release();
      video.pause(); video.removeAttribute('src'); video.load();
      mode = 'overview'; stage.hidden = true; panel.hidden = true; ovEl.hidden = false; note.hidden = true;
      [...list.children].forEach((row, k) => row.classList.toggle('on', k === 0));
      ovEl.innerHTML = `<div class="none">${T('Loading...')}</div>`;
      const mine = seq;
      try {
        const folder = await savedFolder();
        if (mine !== seq) return;
        if (!folder) { ovEl.innerHTML = `<div class="none">${T('Play a replay once to choose the folder.')}</div>`; return; }
        const scenario = replays[0].scenario;
        if (!ovCache.has(scenario)) ovCache.set(scenario, JSON.parse(await (await clipIn(folder, 'cache/' + await overviewName(scenario))).text()));
        if (mine !== seq) return;
        const ov = ovCache.get(scenario), rows = runsBy[scenario.toLowerCase()] || [];
        ovEl.innerHTML = tiles(ov) + adviceCard(ov) + '<div class="card2 hist"></div><div class="card2 sens2"></div><div class="card2 spd2"></div>';
        drawHistory = renderHistory(ovEl.querySelector('.hist'), ov, time => { // a click on a score opens that run when its video exists
          const j = replays.findIndex(r => Math.abs(new Date(r.end).getTime() / 1000 - time) < 60);
          if (j >= 0) play(j);
        });
        renderSens(ovEl.querySelector('.sens2'), ov);
        renderSpeedAll(ovEl.querySelector('.spd2'), scenario, rows);
      } catch { if (mine === seq) ovEl.innerHTML = `<div class="none">${T('No overview yet. Start the app once so it can write it.')}</div>`; }
    }

    async function play(i) {
      release();
      panel.hidden = true; chart.clear(); trail.clear(); compare.clear();
      $('.spd').hidden = true;
      const mine = seq;
      current = i;
      mode = 'run'; ovEl.hidden = true; stage.hidden = false;
      [...list.children].forEach((row, k) => row.classList.toggle('on', k === i + 1));
      video.removeAttribute('src'); video.load();
      if (typeof W.showDirectoryPicker !== 'function') { showNote(T('This browser cannot open a folder of videos. Use Chrome or Edge.'), true); return; }
      showNote(T('Loading the video...'));
      try {
        const folder = await savedFolder();
        if (mine !== seq) return;
        if (!folder) { askFolder(T('Select the folder that has your replay videos:')); return; }
        const file = await clipIn(folder, replays[i].video);
        if (mine !== seq) return;
        blobUrl = URL.createObjectURL(file);
        video.src = blobUrl;
        note.hidden = true;
        video.play().catch(() => {});
        loadPanel(folder, i, mine);
      } catch (e) {
        if (mine !== seq) return;
        if (e && e.name === 'NotFoundError') askFolder(T('The video was not found. Select the folder that has your replay videos:'), true);
        else showNote(T('The video could not be read: ') + (e && e.message || e), true);
      }
    }

    const first = document.createElement('div');
    first.className = 'row first';
    first.innerHTML = `<span>${T('Overview')}</span><span class="n"></span>`;
    first.addEventListener('click', showOverview);
    list.append(first);

    replays.forEach((r, i) => {
      const row = document.createElement('div');
      row.className = 'row';
      const d = document.createElement('span'), n = document.createElement('span');
      d.className = 'd'; n.className = 'n';
      d.textContent = new Date(r.end).toLocaleDateString();
      if (r.pb) { const b = document.createElement('span'); b.className = 'pb'; b.textContent = T('PB'); b.title = T('Personal best'); d.append(b); }
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
