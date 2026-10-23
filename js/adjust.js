'use strict';
/* Trazo — Transformar capa y Ajustes de imagen (con vista previa en vivo). */
(function () {
  const V = T.view;

  // ================= Transformar =================
  const TR = (T.transform = { active: false });
  let anchor = null;

  TR.start = () => {
    const d = T.app.doc;
    if (!d || TR.active) return;
    const L = d.activeLayer;
    if (!L.visible) return T.toast('La capa activa está oculta');
    const b = T.contentBounds(L.canvas);
    if (!b) return T.toast('La capa está vacía');
    T.input.cancelAll();
    T.ui.closePopover();
    TR.active = true;
    TR.layer = L;
    TR.bounds = b;
    TR.src = T.canvas(d.width, d.height);
    TR.src.getContext('2d').drawImage(L.canvas, 0, 0);
    TR.prev = T.canvas(d.width, d.height);
    TR.m = new DOMMatrix();
    TR.smooth = T.settings.get('transformSmooth', true);
    V.overrides.set(L, TR.prev);
    V.overlays.add(overlay);
    redraw();
    T.emit('transform', true);
    T.ui.sheet({
      title: 'Transformar',
      body: [
        T.ui.chipRow([
          ['flipH', 'Voltear H', () => flip(-1, 1)],
          ['flipV', 'Voltear V', () => flip(1, -1)],
          ['rotate', 'Rotar 90°', () => rotateBy(90)],
          ['fit', 'Ajustar', fitToCanvas],
          ['undo', 'Restablecer', () => { TR.m = new DOMMatrix(); redraw(); }],
        ]),
        T.ui.toggle('Interpolación suave', TR.smooth, (v) => { TR.smooth = v; T.settings.set('transformSmooth', v); redraw(); }),
        T.el('p', { class: 'hint', text: 'Arrastra para mover · pellizca con dos dedos para escalar y girar · rueda = escala, Alt+rueda = giro' }),
      ],
      onCancel: TR.cancel,
      onApply: TR.apply,
    });
  };

  function redraw() {
    const x = TR.prev.getContext('2d');
    x.setTransform(1, 0, 0, 1, 0, 0);
    x.clearRect(0, 0, TR.prev.width, TR.prev.height);
    x.imageSmoothingEnabled = TR.smooth;
    x.imageSmoothingQuality = 'high';
    x.setTransform(TR.m);
    x.drawImage(TR.src, 0, 0);
    x.setTransform(1, 0, 0, 1, 0, 0);
    V.request();
  }

  function corners() {
    const b = TR.bounds;
    return [[b.x, b.y], [b.x + b.w, b.y], [b.x + b.w, b.y + b.h], [b.x, b.y + b.h]].map(([x, y]) => TR.m.transformPoint(new DOMPoint(x, y)));
  }
  function center() {
    const c = corners();
    return { x: (c[0].x + c[2].x) / 2, y: (c[0].y + c[2].y) / 2 };
  }
  function around(c, fn) {
    TR.m = fn(new DOMMatrix().translate(c.x, c.y)).translate(-c.x, -c.y).multiply(TR.m);
    redraw();
  }
  const flip = (sx, sy) => around(center(), (m) => m.scale(sx, sy));
  const rotateBy = (deg) => around(center(), (m) => m.rotate(deg));
  function fitToCanvas() {
    const d = T.app.doc, b = TR.bounds;
    const k = Math.min(d.width / b.w, d.height / b.h);
    TR.m = new DOMMatrix().translate((d.width - b.w * k) / 2, (d.height - b.h * k) / 2).scale(k).translate(-b.x, -b.y);
    redraw();
  }

  function overlay(ctx) {
    if (!TR.active) return;
    const pts = corners().map((p) => V.toScreen(p.x, p.y));
    ctx.strokeStyle = '#3d8bff';
    ctx.lineWidth = 1.5;
    ctx.setLineDash([6, 4]);
    ctx.beginPath();
    pts.forEach((p, i) => (i ? ctx.lineTo(p.x, p.y) : ctx.moveTo(p.x, p.y)));
    ctx.closePath();
    ctx.stroke();
    ctx.setLineDash([]);
    ctx.fillStyle = '#fff';
    pts.forEach((p) => {
      ctx.beginPath();
      ctx.arc(p.x, p.y, 6, 0, Math.PI * 2);
      ctx.fill();
      ctx.stroke();
    });
  }

  const localPt = (e) => {
    const r = V.canvas.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top };
  };
  function setAnchor(ptrs) {
    const list = [...ptrs.values()].slice(0, 2);
    anchor = { m: TR.m, ids: list.map((p) => p.id), pts: list.map((p) => V.toDoc(...Object.values(localPt(p.last)))) };
  }
  TR.down = (e, ptrs) => setAnchor(ptrs);
  TR.up = (e, ptrs) => (ptrs.size ? setAnchor(ptrs) : (anchor = null));
  TR.move = (e, ptrs) => {
    if (!anchor) return;
    const cur = anchor.ids.map((id) => ptrs.get(id)).filter(Boolean).map((p) => V.toDoc(...Object.values(localPt(p.last))));
    if (!cur.length) return;
    const [a0, b0] = anchor.pts, [a1, b1] = cur;
    if (b0 && b1) {
      const c0 = { x: (a0.x + b0.x) / 2, y: (a0.y + b0.y) / 2 }, c1 = { x: (a1.x + b1.x) / 2, y: (a1.y + b1.y) / 2 };
      const k = T.dist(a1.x, a1.y, b1.x, b1.y) / Math.max(1e-3, T.dist(a0.x, a0.y, b0.x, b0.y));
      const da = Math.atan2(b1.y - a1.y, b1.x - a1.x) - Math.atan2(b0.y - a0.y, b0.x - a0.x);
      TR.m = new DOMMatrix().translate(c1.x, c1.y).rotate((da * 180) / Math.PI).scale(k).translate(-c0.x, -c0.y).multiply(anchor.m);
    } else {
      TR.m = new DOMMatrix().translate(a1.x - a0.x, a1.y - a0.y).multiply(anchor.m);
    }
    redraw();
  };
  TR.wheel = (e) => {
    const p = V.toDoc(...Object.values(localPt(e)));
    if (e.altKey) around(p, (m) => m.rotate(e.deltaY > 0 ? 5 : -5));
    else around(p, (m) => m.scale(e.deltaY > 0 ? 1 / 1.08 : 1.08));
  };

  function finish() {
    V.overrides.delete(TR.layer);
    V.overlays.delete(overlay);
    TR.active = false;
    anchor = null;
    T.ui.closeSheet();
    T.emit('transform', false);
    V.request();
  }
  TR.apply = () => {
    if (!TR.active) return;
    const L = TR.layer;
    T.history.pixelOp(L, 'Transformar', (c) => {
      c.save();
      c.globalCompositeOperation = 'copy';
      c.drawImage(TR.prev, 0, 0);
      c.restore();
    });
    finish();
  };
  TR.cancel = () => {
    if (TR.active) finish();
  };

  // ================= Ajustes =================
  const P = (k, label, min, max, v, unit = '') => ({ k, label, min, max, v, unit });
  const defs = [
    { id: 'hsb', name: 'Tono, saturación y brillo', params: [P('h', 'Tono', -180, 180, 0, '°'), P('s', 'Saturación', 0, 200, 100, '%'), P('b', 'Brillo', 0, 200, 100, '%')],
      filter: (p) => `hue-rotate(${p.h}deg) saturate(${p.s}%) brightness(${p.b}%)` },
    { id: 'contrast', name: 'Contraste', params: [P('c', 'Contraste', 0, 300, 100, '%')], filter: (p) => `contrast(${p.c}%)` },
    { id: 'blur', name: 'Desenfoque gaussiano', params: [P('r', 'Radio', 0, 80, 0, ' px')], filter: (p) => `blur(${p.r}px)` },
    { id: 'sharpen', name: 'Enfocar', params: [P('a', 'Intensidad', 0, 300, 0, '%')], pixel: sharpen },
    { id: 'noise', name: 'Ruido', params: [P('a', 'Cantidad', 0, 100, 0, '%'), P('mono', 'Monocromo', 0, 1, 1)], pixel: noise },
    { id: 'invert', name: 'Invertir colores', params: [P('a', 'Intensidad', 0, 100, 100, '%')], filter: (p) => `invert(${p.a}%)` },
    { id: 'gray', name: 'Escala de grises', params: [P('a', 'Intensidad', 0, 100, 100, '%')], filter: (p) => `grayscale(${p.a}%)` },
    { id: 'sepia', name: 'Sepia', params: [P('a', 'Intensidad', 0, 100, 100, '%')], filter: (p) => `sepia(${p.a}%)` },
    { id: 'opacityfx', name: 'Opacidad de píxeles', params: [P('a', 'Opacidad', 0, 100, 100, '%')], filter: (p) => `opacity(${p.a}%)` },
  ];
  T.adjustDefs = defs;

  const AD = (T.adjust = { active: null });

  AD.open = (id) => {
    const d = T.app.doc;
    const def = defs.find((x) => x.id === id);
    const L = d.activeLayer;
    if (!L.visible) return T.toast('La capa activa está oculta');
    T.input.cancelAll();
    T.ui.closePopover();
    const st = { def, layer: L, vals: {}, src: T.canvas(d.width, d.height), prev: T.canvas(d.width, d.height), cache: {} };
    st.src.getContext('2d').drawImage(L.canvas, 0, 0);
    def.params.forEach((p) => (st.vals[p.k] = p.v));
    AD.active = st;
    T.input.blocked = true;
    V.overrides.set(L, st.prev);
    render();
    const rows = def.params.map((p) =>
      p.max === 1 && p.min === 0
        ? T.ui.toggle(p.label, !!p.v, (v) => { st.vals[p.k] = v ? 1 : 0; renderSoon(); })
        : T.ui.slider(p.label, p.min, p.max, p.v, p.unit, (v) => { st.vals[p.k] = v; renderSoon(); }));
    T.ui.sheet({ title: def.name, body: rows, onCancel: AD.cancel, onApply: AD.apply });
  };

  const renderSoon = T.debounce(() => render(), 16);
  function render() {
    const st = AD.active;
    if (!st) return;
    const x = st.prev.getContext('2d');
    x.save();
    x.globalCompositeOperation = 'copy';
    if (st.def.filter) {
      x.filter = st.def.filter(st.vals);
      x.drawImage(st.src, 0, 0);
    } else {
      st.def.pixel(st, x);
    }
    x.restore();
    V.request();
  }

  function sharpen(st, x) {
    const W = st.src.width, H = st.src.height;
    if (!st.cache.orig) {
      st.cache.orig = st.src.getContext('2d').getImageData(0, 0, W, H);
      const b = T.canvas(W, H), bx = b.getContext('2d');
      bx.filter = 'blur(2px)';
      bx.drawImage(st.src, 0, 0);
      st.cache.blur = bx.getImageData(0, 0, W, H).data;
      st.cache.out = new ImageData(W, H);
    }
    const o = st.cache.orig.data, bl = st.cache.blur, out = st.cache.out.data, k = st.vals.a / 100;
    for (let i = 0; i < o.length; i += 4) {
      out[i] = o[i] + (o[i] - bl[i]) * k;
      out[i + 1] = o[i + 1] + (o[i + 1] - bl[i + 1]) * k;
      out[i + 2] = o[i + 2] + (o[i + 2] - bl[i + 2]) * k;
      out[i + 3] = o[i + 3];
    }
    x.putImageData(st.cache.out, 0, 0);
  }
  function noise(st, x) {
    const W = st.src.width, H = st.src.height;
    if (!st.cache.orig) {
      st.cache.orig = st.src.getContext('2d').getImageData(0, 0, W, H);
      st.cache.out = new ImageData(W, H);
    }
    const o = st.cache.orig.data, out = st.cache.out.data, a = st.vals.a * 1.6, mono = st.vals.mono;
    for (let i = 0; i < o.length; i += 4) {
      if (!o[i + 3]) { out[i + 3] = 0; continue; }
      const n = (Math.random() - 0.5) * a;
      out[i] = o[i] + n;
      out[i + 1] = o[i + 1] + (mono ? n : (Math.random() - 0.5) * a);
      out[i + 2] = o[i + 2] + (mono ? n : (Math.random() - 0.5) * a);
      out[i + 3] = o[i + 3];
    }
    x.putImageData(st.cache.out, 0, 0);
  }

  function close() {
    const st = AD.active;
    if (!st) return;
    V.overrides.delete(st.layer);
    AD.active = null;
    T.input.blocked = false;
    T.ui.closeSheet();
    V.request();
  }
  AD.apply = () => {
    const st = AD.active;
    if (!st) return;
    renderSoon.cancel();
    render();
    T.history.pixelOp(st.layer, st.def.name, (c) => {
      c.save();
      c.globalCompositeOperation = 'copy';
      c.drawImage(st.prev, 0, 0);
      c.restore();
    });
    close();
  };
  AD.cancel = close;
})();
