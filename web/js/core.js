'use strict';
/* Trazo — núcleo: utilidades, eventos, color, ajustes persistentes e iconos. */
(function () {
  const T = (window.T = {});

  T.clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  T.lerp = (a, b, t) => a + (b - a) * t;
  T.dist = (ax, ay, bx, by) => Math.hypot(bx - ax, by - ay);
  T.uid = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 8);
  T.canvas = (w, h) => {
    const c = document.createElement('canvas');
    c.width = Math.max(1, Math.round(w));
    c.height = Math.max(1, Math.round(h));
    return c;
  };
  T.$ = (s, r = document) => r.querySelector(s);
  T.$$ = (s, r = document) => Array.from(r.querySelectorAll(s));

  // Crea elementos: T.el('div', {class:'x', onclick: fn}, hijo1, hijo2)
  T.el = (tag, props, ...kids) => {
    const e = document.createElement(tag);
    for (const k in props || {}) {
      const v = props[k];
      if (v == null || v === false) continue;
      if (k === 'class') e.className = v;
      else if (k === 'html') e.innerHTML = v;
      else if (k === 'text') e.textContent = v;
      else if (k === 'style' && typeof v === 'object') Object.assign(e.style, v);
      else if (k.startsWith('on')) e.addEventListener(k.slice(2), v);
      else if (k in e && typeof v !== 'string') e[k] = v;
      else e.setAttribute(k, v === true ? '' : v);
    }
    const add = (c) => {
      if (c == null || c === false) return;
      if (Array.isArray(c)) c.forEach(add);
      else e.append(c.nodeType ? c : document.createTextNode(String(c)));
    };
    kids.forEach(add);
    return e;
  };

  const handlers = {};
  T.on = (n, f) => (handlers[n] || (handlers[n] = [])).push(f);
  T.emit = (n, ...a) => (handlers[n] || []).forEach((f) => f(...a));

  T.debounce = (fn, ms) => {
    let t;
    const d = (...a) => {
      clearTimeout(t);
      t = setTimeout(() => fn(...a), ms);
    };
    d.cancel = () => clearTimeout(t);
    return d;
  };

  // ---- Color ----
  T.hsvToRgb = (h, s, v) => {
    h = (((h % 360) + 360) % 360) / 60;
    const i = Math.floor(h), f = h - i;
    const p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
    const m = [[v, t, p], [q, v, p], [p, v, t], [p, q, v], [t, p, v], [v, p, q]][i];
    return m.map((x) => Math.round(x * 255));
  };
  T.rgbToHsv = (r, g, b) => {
    r /= 255; g /= 255; b /= 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b), d = max - min;
    let h = 0;
    if (d) {
      if (max === r) h = ((g - b) / d) % 6;
      else if (max === g) h = (b - r) / d + 2;
      else h = (r - g) / d + 4;
      h *= 60;
      if (h < 0) h += 360;
    }
    return [h, max ? d / max : 0, max];
  };
  T.rgbToHex = (rgb) => '#' + rgb.map((x) => T.clamp(Math.round(x), 0, 255).toString(16).padStart(2, '0')).join('');
  T.hexToRgb = (hex) => {
    const m = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/i.exec((hex || '').trim());
    if (!m) return null;
    let s = m[1];
    if (s.length === 3) s = s.split('').map((c) => c + c).join('');
    return [0, 2, 4].map((i) => parseInt(s.slice(i, i + 2), 16));
  };
  T.hsvToHex = (h, s, v) => T.rgbToHex(T.hsvToRgb(h, s, v));

  // ---- Ajustes persistentes ----
  const KEY = 'trazo.settings';
  let S = {};
  try { S = JSON.parse(localStorage.getItem(KEY)) || {}; } catch (e) { S = {}; }
  const persist = T.debounce(() => {
    try { localStorage.setItem(KEY, JSON.stringify(S)); } catch (e) { /* almacenamiento no disponible */ }
  }, 200);
  T.settings = {
    get: (k, d) => (k in S ? S[k] : d),
    has: (k) => k in S,
    set: (k, v) => { S[k] = v; persist(); T.emit('setting', k, v); },
  };

  // ---- Toast ----
  let toastTimer;
  T.toast = (msg, ms = 1800) => {
    const t = T.$('#toast');
    t.textContent = msg;
    t.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => t.classList.remove('show'), ms);
  };

  // ---- Descargas ----
  T.download = (blob, name) => {
    const a = T.el('a', { href: URL.createObjectURL(blob), download: name });
    document.body.append(a);
    a.click();
    setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 2000);
  };
  T.toBlob = (canvas, type = 'image/png', q) =>
    new Promise((res) => canvas.toBlob((b) => res(b), type, q));
  T.safeName = (s) => (s || 'trazo').replace(/[\\/:*?"<>|]+/g, '_').trim() || 'trazo';

  // ---- Iconos (SVG en línea, trazo currentColor) ----
  const P = {
    gallery: '<rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/>',
    wrench: '<path d="M14.7 6.3a4 4 0 0 0 5 5L21 10a5.5 5.5 0 0 1-7.4 5.2L7 21.8a2 2 0 0 1-2.8-2.8l6.6-6.6A5.5 5.5 0 0 1 16 5z"/>',
    wand: '<path d="M4 20 15 9M14 4v2M14 12v2M10 8h2M17 8h2M17.5 4.5 16 6M12 6 10.5 4.5M17.5 11.5 16 10"/>',
    cursor: '<path d="M5 3l14 7-6 2-2 6z"/><path d="m13 12 6 6"/>',
    brush: '<path d="M19 3a2 2 0 0 1 2.8 2.8L12 15.6 9.2 12.8z"/><path d="M8 14c-2.2 0-4 1.7-4 4 0 1.2-.6 2-1.5 2.4 1 .6 2.2.9 3.5.9 2.8 0 5-2.2 5-5z"/>',
    smudge: '<path d="M9 11V5a1.5 1.5 0 0 1 3 0v5m0-.5a1.5 1.5 0 0 1 3 0V11m0-.5a1.5 1.5 0 0 1 3 0V15a6 6 0 0 1-6 6h-1a6 6 0 0 1-5-2.7L3.6 14a1.5 1.5 0 0 1 2.6-1.5L9 15"/>',
    eraser: '<path d="M7 21h13"/><path d="M4.6 15.4 14 6a2 2 0 0 1 2.8 0l2.5 2.5a2 2 0 0 1 0 2.8L11 19.6a2 2 0 0 1-1.4.6H8.3a2 2 0 0 1-1.4-.6l-2.3-2.3a1.6 1.6 0 0 1 0-1.9z"/><path d="m9.5 10.5 5.5 5.5"/>',
    layers: '<path d="m12 3 9 5-9 5-9-5z"/><path d="m3 12.5 9 5 9-5"/><path d="m3 17 9 5 9-5"/>',
    undo: '<path d="M9 14 4 9l5-5"/><path d="M4 9h11a5 5 0 0 1 0 10h-3"/>',
    redo: '<path d="m15 14 5-5-5-5"/><path d="M20 9H9a5 5 0 0 0 0 10h3"/>',
    eyedrop: '<path d="m2 22 1-1h3l9-9"/><path d="M3 21v-3l9-9"/><path d="m15 6 3.4-3.4a2.1 2.1 0 1 1 3 3L18 9l.4.4a2.1 2.1 0 1 1-3 3l-3.8-3.8a2.1 2.1 0 1 1 3-3z"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    more: '<circle cx="5" cy="12" r="1.3"/><circle cx="12" cy="12" r="1.3"/><circle cx="19" cy="12" r="1.3"/>',
    back: '<path d="m15 18-6-6 6-6"/>',
    close: '<path d="M18 6 6 18M6 6l12 12"/>',
    check: '<path d="M20 6 9 17l-5-5"/>',
    trash: '<path d="M3 6h18M8 6V4h8v2M6 6l1 14h10l1-14"/>',
    image: '<rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="9" cy="9" r="2"/><path d="m21 15-5-5L5 21"/>',
    share: '<path d="M12 3v12M7 8l5-5 5 5"/><path d="M5 14v5a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-5"/>',
    canvas: '<rect x="3" y="4" width="18" height="14" rx="1.5"/><path d="M8 21h8"/>',
    prefs: '<path d="M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12M20 18h0"/><circle cx="16" cy="6" r="2"/><circle cx="10" cy="12" r="2"/><circle cx="18" cy="18" r="2"/>',
    help: '<circle cx="12" cy="12" r="9"/><path d="M9.5 9a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .9-1 1.6V14"/><path d="M12 17.5v.01"/>',
    lock: '<rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>',
    clip: '<path d="M7 4v10a3 3 0 0 0 3 3h10"/><path d="m16 13 4 4-4 4"/>',
    grip: '<path d="M5 9h14M5 15h14"/>',
    flipH: '<path d="M12 3v18"/><path d="M8 7 3 12l5 5z"/><path d="m16 7 5 5-5 5z"/>',
    flipV: '<path d="M3 12h18"/><path d="M7 8l5-5 5 5z"/><path d="m7 16 5 5 5-5z"/>',
    rotate: '<path d="M21 12a9 9 0 1 1-3-6.7L21 8"/><path d="M21 3v5h-5"/>',
    fit: '<path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5"/>',
    sym: '<path d="M12 2v20" stroke-dasharray="2 2"/><path d="M9 7c-3 1-4 4-3 7M15 7c3 1 4 4 3 7"/>',
    fill: '<path d="m19 11-8-8-8.5 8.5a2 2 0 0 0 0 2.8l5.2 5.2a2 2 0 0 0 2.8 0z"/><path d="M5 2l5 5"/><path d="M2 13h17"/><path d="M22 20a2 2 0 1 1-4 0c0-1.6 2-4 2-4s2 2.4 2 4z"/>',
    download: '<path d="M12 3v12M7 10l5 5 5-5"/><path d="M5 21h14"/>',
    upload: '<path d="M12 21V9M7 14l5-5 5 5"/><path d="M5 3h14"/>',
    copy: '<rect x="8" y="8" width="13" height="13" rx="2"/><path d="M16 8V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h3"/>',
    merge: '<path d="M8 3v6l4 4 4-4V3"/><path d="M12 13v8"/>',
    invert: '<circle cx="12" cy="12" r="9"/><path d="M12 3v18a9 9 0 0 0 0-18z" fill="currentColor"/>',
    text: '<path d="M4 7V4h16v3M9 20h6M12 4v16"/>',
    up: '<path d="m18 15-6-6-6 6"/>',
    down: '<path d="m6 9 6 6 6-6"/>',
    eye: '<path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
    hand: '<path d="M18 11V6a2 2 0 0 0-4 0v5M14 10V4a2 2 0 0 0-4 0v6M10 10.5V6a2 2 0 0 0-4 0v8"/><path d="M18 8a2 2 0 1 1 4 0v6a8 8 0 0 1-8 8h-2c-2.8 0-4.5-.9-6-2.4l-3.6-3.6a2 2 0 0 1 2.8-2.8L7 15"/>',
  };
  T.icon = (name) =>
    `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${P[name] || ''}</svg>`;
  T.hydrateIcons = (root = document) => {
    T.$$('[data-icon]', root).forEach((e) => {
      if (!e.querySelector('svg')) e.insertAdjacentHTML('afterbegin', T.icon(e.dataset.icon));
    });
  };
})();
