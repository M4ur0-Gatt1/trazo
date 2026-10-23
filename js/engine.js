'use strict';
/* Trazo — motor de pintura: herramientas, trazos con estampado, difuminar,
   QuickShape, simetría, relleno por color y cuentagotas. */
(function () {
  const E = (T.engine = { active: null });
  const TS = () => T.TIP;

  // ---- Estado de herramientas (persistente) ----
  const saved = T.settings.get('tools', {});
  const tools = (T.tools = Object.assign({
    current: 'brush',
    brush: { brush: 'studiopen', size: 7, opacity: 1 },
    smudge: { brush: 'roundbrush', size: 40, opacity: 0.6 },
    eraser: { brush: 'softair', size: 80, opacity: 1 },
    color: '#1f1f24',
    hsv: [240, 0.14, 0.14],
    prevColor: '#1f1f24',
    recent: [],
    sizes: {},
    symmetry: 'none',
  }, saved));
  ['brush', 'smudge', 'eraser'].forEach((k) => {
    if (!T.brushes[tools[k].brush]) tools[k].brush = 'roundbrush';
  });
  const persist = T.debounce(() => T.settings.set('tools', tools), 300);

  E.state = () => tools[tools.current];
  E.brush = (tool) => T.brushes[tools[tool || tools.current].brush];

  E.setTool = (name, silent) => {
    tools.current = name;
    if (!silent) persist();
    T.emit('tool');
  };
  E.setBrush = (tool, id) => {
    const st = tools[tool];
    tools.sizes[tool + ':' + st.brush] = st.size;
    st.brush = id;
    st.size = tools.sizes[tool + ':' + id] || T.brushes[id].size;
    persist();
    T.emit('tool');
  };
  E.setSize = (px) => {
    const st = E.state(), b = E.brush();
    st.size = T.clamp(px, b.minSize, b.maxSize);
    tools.sizes[tools.current + ':' + st.brush] = st.size;
    persist();
    T.emit('toolvalues');
  };
  E.setOpacity = (v) => {
    E.state().opacity = T.clamp(v, 0.01, 1);
    persist();
    T.emit('toolvalues');
  };
  E.setColor = (hex, hsv) => {
    if (!hex) return;
    hex = hex.toLowerCase();
    if (hex !== tools.color) tools.prevColor = tools.color;
    tools.color = hex;
    tools.hsv = hsv || T.rgbToHsv(...T.hexToRgb(hex));
    persist();
    T.emit('color');
  };
  E.pushRecent = (hex) => {
    const r = tools.recent.filter((c) => c !== hex);
    r.unshift(hex);
    tools.recent = r.slice(0, 12);
    persist();
  };
  E.setSymmetry = (m) => {
    tools.symmetry = m;
    persist();
    T.view.request();
    T.emit('symmetry');
  };

  // ---- Lienzos compartidos de trabajo ----
  let buf, bctx, prev, pctx;
  function ensure(d) {
    if (!buf || buf.width !== d.width || buf.height !== d.height) {
      buf = T.canvas(d.width, d.height);
      bctx = buf.getContext('2d');
      prev = T.canvas(d.width, d.height);
      pctx = prev.getContext('2d');
    }
  }

  // ---- Simetría ----
  E.symPoints = (x, y, r) => {
    const d = T.app.doc, cx = d.width / 2, cy = d.height / 2, m = tools.symmetry;
    const out = [{ x, y, r }];
    if (m === 'vertical' || m === 'quadrant') out.push({ x: 2 * cx - x, y, r: Math.PI - r });
    if (m === 'horizontal' || m === 'quadrant') out.push({ x, y: 2 * cy - y, r: -r });
    if (m === 'quadrant') out.push({ x: 2 * cx - x, y: 2 * cy - y, r: r + Math.PI });
    if (m === 'radial') {
      const n = 8, dx = x - cx, dy = y - cy;
      for (let i = 1; i < n; i++) {
        const t = (i * 2 * Math.PI) / n, c = Math.cos(t), s = Math.sin(t);
        out.push({ x: cx + dx * c - dy * s, y: cy + dx * s + dy * c, r: r + t });
      }
    }
    return out;
  };

  // ---- Trazo ----
  class Stroke {
    constructor(p) {
      const d = (this.doc = T.app.doc);
      ensure(d);
      this.layer = d.activeLayer;
      this.tool = tools.current;
      this.st = tools[this.tool];
      this.brush = T.brushes[this.st.brush];
      this.color = this.tool === 'eraser' ? '#000000' : tools.color;
      this.size = this.st.size;
      this.opacity = this.st.opacity;
      this.smudge = this.tool === 'smudge';
      this.spacing = this.smudge ? Math.min(this.brush.spacing, 0.06) : this.brush.spacing;
      this.flow = this.brush.flow;
      this.tint = T.tintedTip(this.brush.tip, this.color);
      this.R = (this.brush.streamline * 26) / T.view.scale; // radio de StreamLine en px del documento
      this.raw = [];
      this.sm = null;
      this.last = null;
      this.carry = 0;
      this.bbox = null;
      this.shape = null;
      this.dirtyPrev = true;
      bctx.setTransform(1, 0, 0, 1, 0, 0);
      bctx.clearRect(0, 0, buf.width, buf.height);
      T.history.snapshot(this.layer);
      if (!this.smudge) T.view.overrides.set(this.layer, prev);
      this.add(p);
    }

    sizeAt(p) {
      return this.size * T.lerp(this.brush.minSizeP, 1, p);
    }

    grow(x, y, s) {
      const h = s / 2 + 2;
      const b = this.bbox;
      if (!b) this.bbox = { x: x - h, y: y - h, x2: x + h, y2: y + h };
      else {
        if (x - h < b.x) b.x = x - h;
        if (y - h < b.y) b.y = y - h;
        if (x + h > b.x2) b.x2 = x + h;
        if (y + h > b.y2) b.y2 = y + h;
      }
    }

    add(p) {
      this.raw.push(p);
      if (this.shape) {
        this.updateShape(p);
        return;
      }
      if (!this.sm) {
        this.sm = { x: p.x, y: p.y, p: p.p, tilt: p.tilt };
        this.segTo(this.sm);
        return;
      }
      // StreamLine tipo "cuerda": el punto suavizado solo avanza cuando el lápiz se aleja más de R
      const dx = p.x - this.sm.x, dy = p.y - this.sm.y, dd = Math.hypot(dx, dy);
      if (dd > this.R) {
        const f = this.R ? (dd - this.R) / dd : 1;
        this.sm = { x: this.sm.x + dx * f, y: this.sm.y + dy * f, p: p.p, tilt: p.tilt };
        this.segTo(this.sm);
      }
    }

    segTo(p) {
      const a = this.last;
      if (!a) {
        this.stamp(p.x, p.y, p.p, p.tilt || 0, 0);
        this.carry = Math.max(0.35, this.sizeAt(p.p) * this.spacing);
        this.last = { x: p.x, y: p.y, p: p.p, tilt: p.tilt || 0 };
        this.finishDabs();
        return;
      }
      const dx = p.x - a.x, dy = p.y - a.y, len = Math.hypot(dx, dy);
      if (len < 1e-3) return;
      const ang = Math.atan2(dy, dx);
      let t = this.carry;
      while (t <= len) {
        const u = t / len;
        const pr = T.lerp(a.p, p.p, u);
        this.stamp(a.x + dx * u, a.y + dy * u, pr, T.lerp(a.tilt, p.tilt || 0, u), ang);
        t += Math.max(0.35, this.sizeAt(pr) * this.spacing);
      }
      this.carry = t - len;
      this.last = { x: p.x, y: p.y, p: p.p, tilt: p.tilt || 0 };
      this.finishDabs();
    }

    finishDabs() {
      bctx.setTransform(1, 0, 0, 1, 0, 0);
      this.dirtyPrev = true;
      T.view.request();
    }

    stamp(x, y, p, tilt, ang) {
      const b = this.brush;
      let s = this.sizeAt(p);
      if (b.tilt && tilt) s *= 1 + b.tilt * tilt * tilt;
      if (b.jitter) s *= 1 + (Math.random() * 2 - 1) * b.jitter;
      s = Math.max(0.4, s);
      if (b.scatter) {
        const r = Math.random() * b.scatter * s * 2, th = Math.random() * 6.2832;
        x += Math.cos(th) * r;
        y += Math.sin(th) * r;
      }
      if (this.smudge) {
        this.smudgeAt(x, y, s, p);
        return;
      }
      let a = this.flow * T.lerp(1 - b.flowP, 1, p);
      if (s < 1.5) a *= s / 1.5;
      const rot = b.angle + (b.rotFollow ? ang : 0) + (b.rotRand ? (Math.random() * 2 - 1) * b.rotRand : 0);
      const pts = tools.symmetry === 'none' ? [{ x, y, r: rot }] : E.symPoints(x, y, rot);
      const k = s / TS();
      bctx.globalAlpha = a;
      for (const q of pts) {
        const c = Math.cos(q.r) * k, sn = Math.sin(q.r) * k;
        bctx.setTransform(c, sn, -sn, c, q.x, q.y);
        bctx.drawImage(this.tint, -TS() / 2, -TS() / 2);
        this.grow(q.x, q.y, s * 1.5);
      }
    }

    // Difuminar: arrastra un "parche" de píxeles que se mezcla con lo que encuentra
    smudgeAt(x, y, s, p) {
      const L = this.layer.ctx, src = this.layer.canvas;
      const S = Math.max(4, Math.ceil(this.size));
      if (!this.patch) {
        this.patch = T.canvas(S, S);
        this.patchCtx = this.patch.getContext('2d');
        this.mask = T.canvas(S, S);
        this.maskCtx = this.mask.getContext('2d');
        this.patchCtx.drawImage(src, x - S / 2, y - S / 2, S, S, 0, 0, S, S);
        this.grow(x, y, S);
        return;
      }
      const m = this.maskCtx;
      m.globalCompositeOperation = 'copy';
      m.drawImage(this.patch, 0, 0);
      m.globalCompositeOperation = 'destination-in';
      m.drawImage(T.tips[this.brush.tip], 0, 0, S, S);
      L.save();
      if (this.layer.alphaLock) L.globalCompositeOperation = 'source-atop';
      L.globalAlpha = T.clamp(this.opacity * T.lerp(0.35, 1, p) * 0.55, 0, 1);
      L.drawImage(this.mask, x - s / 2, y - s / 2, s, s);
      L.restore();
      // recoge un poco del color de debajo
      const pc = this.patchCtx;
      pc.globalAlpha = T.lerp(0.35, 0.05, this.opacity);
      pc.drawImage(src, x - S / 2, y - S / 2, S, S, 0, 0, S, S);
      pc.globalAlpha = 1;
      this.grow(x, y, s);
    }

    updatePreview() {
      if (this.smudge || !this.dirtyPrev) return;
      this.dirtyPrev = false;
      pctx.globalCompositeOperation = 'copy';
      pctx.globalAlpha = 1;
      pctx.drawImage(this.layer.canvas, 0, 0);
      pctx.globalAlpha = this.opacity;
      pctx.globalCompositeOperation = this.compositeOp();
      pctx.drawImage(buf, 0, 0);
      pctx.globalAlpha = 1;
      pctx.globalCompositeOperation = 'source-over';
    }

    compositeOp() {
      if (this.tool === 'eraser') return 'destination-out';
      return this.layer.alphaLock ? 'source-atop' : 'source-over';
    }

    end() {
      if (!this.shape && this.raw.length > 1 && this.R > 0) {
        const r = this.raw[this.raw.length - 1];
        this.segTo({ x: r.x, y: r.y, p: this.sm ? this.sm.p : r.p, tilt: r.tilt });
      }
      const L = this.layer;
      if (!this.smudge && this.bbox) {
        const r = T.history.clipRect({ x: this.bbox.x, y: this.bbox.y, w: this.bbox.x2 - this.bbox.x, h: this.bbox.y2 - this.bbox.y });
        if (r) {
          const c = L.ctx;
          c.save();
          c.globalAlpha = this.opacity;
          c.globalCompositeOperation = this.compositeOp();
          c.drawImage(buf, r.x, r.y, r.w, r.h, r.x, r.y, r.w, r.h);
          c.restore();
        }
      }
      T.view.overrides.delete(L);
      if (this.bbox) {
        const names = { brush: 'Pintar', eraser: 'Borrar', smudge: 'Difuminar' };
        T.history.commitPixels(L, { x: this.bbox.x, y: this.bbox.y, w: this.bbox.x2 - this.bbox.x, h: this.bbox.y2 - this.bbox.y }, names[this.tool]);
      }
      if (this.tool === 'brush') E.pushRecent(this.color);
      E.active = null;
      T.view.overlays.delete(shapeLabel);
      T.view.request();
    }

    cancel() {
      T.view.overrides.delete(this.layer);
      bctx.setTransform(1, 0, 0, 1, 0, 0);
      bctx.clearRect(0, 0, buf.width, buf.height);
      if (this.smudge) {
        const c = this.layer.ctx;
        c.save();
        c.globalCompositeOperation = 'copy';
        c.drawImage(T.history.backupCanvas(), 0, 0);
        c.restore();
      }
      E.active = null;
      T.view.overlays.delete(shapeLabel);
      T.view.request();
    }

    // ---- QuickShape: mantener el lápiz quieto al final convierte el trazo en forma ----
    snap() {
      if (this.smudge || this.shape || this.raw.length < 4) return false;
      const pts = this.raw;
      const a = pts[0], b = pts[pts.length - 1];
      let len = 0, x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity, psum = 0;
      for (let i = 0; i < pts.length; i++) {
        const q = pts[i];
        if (i) len += T.dist(pts[i - 1].x, pts[i - 1].y, q.x, q.y);
        x0 = Math.min(x0, q.x); y0 = Math.min(y0, q.y); x1 = Math.max(x1, q.x); y1 = Math.max(y1, q.y);
        psum += q.p;
      }
      const diag = Math.hypot(x1 - x0, y1 - y0);
      if (diag * T.view.scale < 24) return false;
      const chord = T.dist(a.x, a.y, b.x, b.y);
      let maxDev = 0;
      if (chord > 0) {
        for (const q of pts) {
          const dev = Math.abs((b.x - a.x) * (a.y - q.y) - (a.x - q.x) * (b.y - a.y)) / chord;
          if (dev > maxDev) maxDev = dev;
        }
      }
      this.shapeP = Math.max(0.35, psum / pts.length);
      if (chord > 0 && maxDev < Math.max(6 / T.view.scale, chord * 0.08)) {
        this.shape = { type: 'line', a: { x: a.x, y: a.y }, b: { x: b.x, y: b.y } };
      } else if (chord < diag * 0.3) {
        const cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        let rx = (x1 - x0) / 2, ry = (y1 - y0) / 2;
        const circle = Math.abs(rx - ry) / Math.max(rx, ry) < 0.15;
        if (circle) rx = ry = (rx + ry) / 2;
        this.shape = { type: circle ? 'circle' : 'ellipse', cx, cy, rx, ry, rx0: rx, ry0: ry, d0: Math.max(1, T.dist(cx, cy, b.x, b.y)) };
      } else {
        this.shape = { type: 'poly', pts: rdp(pts, diag * 0.035) };
      }
      this.redrawShape();
      T.view.overlays.add(shapeLabel);
      return true;
    }

    shapePath() {
      const s = this.shape;
      if (s.type === 'line') return [s.a, s.b];
      if (s.type === 'poly') return s.pts;
      const out = [], n = Math.max(48, Math.ceil(Math.max(s.rx, s.ry) * 0.6));
      for (let i = 0; i <= n; i++) {
        const t = (i / n) * Math.PI * 2;
        out.push({ x: s.cx + Math.cos(t) * s.rx, y: s.cy + Math.sin(t) * s.ry });
      }
      return out;
    }

    redrawShape() {
      bctx.setTransform(1, 0, 0, 1, 0, 0);
      bctx.clearRect(0, 0, buf.width, buf.height);
      this.bbox = null;
      this.last = null;
      this.carry = 0;
      for (const q of this.shapePath()) this.segTo({ x: q.x, y: q.y, p: this.shapeP, tilt: 0 });
    }

    updateShape(p) {
      const s = this.shape;
      if (s.type === 'line') s.b = { x: p.x, y: p.y };
      else if (s.type !== 'poly') {
        const k = T.dist(s.cx, s.cy, p.x, p.y) / s.d0;
        s.rx = s.rx0 * k;
        s.ry = s.ry0 * k;
      } else return;
      this.redrawShape();
    }
  }

  function shapeLabel(ctx) {
    const s = E.active && E.active.shape;
    if (!s) return;
    const names = { line: 'Línea', circle: 'Círculo', ellipse: 'Elipse', poly: 'Polilínea' };
    const txt = names[s.type] + ' creada';
    ctx.font = '600 13px "Segoe UI", system-ui, sans-serif';
    const w = ctx.measureText(txt).width + 24;
    const x = (T.view.W - w) / 2, y = 64;
    ctx.fillStyle = 'rgba(20,20,22,.9)';
    ctx.beginPath();
    ctx.roundRect(x, y, w, 30, 15);
    ctx.fill();
    ctx.fillStyle = '#fff';
    ctx.fillText(txt, x + 12, y + 20);
  }

  // Simplificación Ramer–Douglas–Peucker
  function rdp(pts, eps) {
    if (pts.length < 3) return pts.slice();
    const a = pts[0], b = pts[pts.length - 1];
    const L = T.dist(a.x, a.y, b.x, b.y) || 1e-6;
    let idx = 0, md = 0;
    for (let i = 1; i < pts.length - 1; i++) {
      const q = pts[i];
      const d = L > 1e-5
        ? Math.abs((b.x - a.x) * (a.y - q.y) - (a.x - q.x) * (b.y - a.y)) / L
        : T.dist(a.x, a.y, q.x, q.y);
      if (d > md) { md = d; idx = i; }
    }
    if (md <= eps) return [a, b];
    const l = rdp(pts.slice(0, idx + 1), eps), r = rdp(pts.slice(idx), eps);
    return l.slice(0, -1).concat(r);
  }

  E.begin = (p) => {
    const d = T.app.doc;
    const l = d && d.activeLayer;
    if (!l) return null;
    if (!l.visible) {
      T.toast('La capa activa está oculta');
      return null;
    }
    E.active = new Stroke(p);
    return E.active;
  };

  T.on('prerender', () => {
    if (E.active) E.active.updatePreview();
  });

  // ---- Cuentagotas ----
  const one = T.canvas(1, 1), onex = one.getContext('2d', { willReadFrequently: true });
  E.sample = (x, y) => {
    const d = T.app.doc;
    if (!d || x < 0 || y < 0 || x >= d.width || y >= d.height) return null;
    onex.setTransform(1, 0, 0, 1, 0, 0);
    onex.clearRect(0, 0, 1, 1);
    onex.setTransform(1, 0, 0, 1, -Math.floor(x), -Math.floor(y));
    d.composite(onex, T.view.overrides);
    const p = onex.getImageData(0, 0, 1, 1).data;
    if (p[3] === 0) return null;
    // si hay transparencia sin fondo, se muestra el color sin premultiplicar
    return T.rgbToHex([p[0], p[1], p[2]]);
  };

  // ---- Relleno por color (ColorDrop): referencia = imagen compuesta visible ----
  E.fill = (dx, dy, color) => {
    const d = T.app.doc, L = d.activeLayer;
    if (!L.visible) return T.toast('La capa activa está oculta');
    const W = d.width, H = d.height;
    const sx = Math.floor(dx), sy = Math.floor(dy);
    if (sx < 0 || sy < 0 || sx >= W || sy >= H) return;
    const ref = d.flatten();
    const rd = ref.getContext('2d').getImageData(0, 0, W, H).data;
    const o = (sy * W + sx) * 4;
    const tr = rd[o], tg = rd[o + 1], tb = rd[o + 2], ta = rd[o + 3];
    const tol = T.settings.get('fillThreshold', 18) * 2.55;
    const match = (i) => {
      const j = i * 4;
      return Math.abs(rd[j] - tr) <= tol && Math.abs(rd[j + 1] - tg) <= tol &&
        Math.abs(rd[j + 2] - tb) <= tol && Math.abs(rd[j + 3] - ta) <= tol;
    };
    const mask = new Uint8Array(W * H);
    const stack = [sx, sy];
    let x0 = sx, x1 = sx, y0 = sy, y1 = sy;
    while (stack.length) {
      const y = stack.pop();
      let x = stack.pop();
      let i = y * W + x;
      while (x >= 0 && !mask[i] && match(i)) { x--; i--; }
      x++; i++;
      let up = false, dn = false;
      while (x < W && !mask[i] && match(i)) {
        mask[i] = 1;
        if (x < x0) x0 = x;
        if (x > x1) x1 = x;
        if (y < y0) y0 = y;
        if (y > y1) y1 = y;
        if (y > 0) {
          const m = !mask[i - W] && match(i - W);
          if (m && !up) { stack.push(x, y - 1); up = true; } else if (!m) up = false;
        }
        if (y < H - 1) {
          const m = !mask[i + W] && match(i + W);
          if (m && !dn) { stack.push(x, y + 1); dn = true; } else if (!m) dn = false;
        }
        x++; i++;
      }
    }
    // expande 1 px para cubrir el antialias de las líneas
    x0 = Math.max(0, x0 - 1); y0 = Math.max(0, y0 - 1);
    x1 = Math.min(W - 1, x1 + 1); y1 = Math.min(H - 1, y1 + 1);
    const bw = x1 - x0 + 1, bh = y1 - y0 + 1;
    const img = new ImageData(bw, bh);
    const [r, g, b] = T.hexToRgb(color);
    for (let y = y0; y <= y1; y++) {
      for (let x = x0; x <= x1; x++) {
        const i = y * W + x;
        const hit = mask[i] || (x > 0 && mask[i - 1]) || (x < W - 1 && mask[i + 1]) ||
          (y > 0 && mask[i - W]) || (y < H - 1 && mask[i + W]);
        if (hit) {
          const j = ((y - y0) * bw + (x - x0)) * 4;
          img.data[j] = r; img.data[j + 1] = g; img.data[j + 2] = b; img.data[j + 3] = 255;
        }
      }
    }
    const tmp = T.canvas(bw, bh);
    tmp.getContext('2d').putImageData(img, 0, 0);
    T.history.snapshot(L);
    L.ctx.save();
    if (L.alphaLock) L.ctx.globalCompositeOperation = 'source-atop';
    L.ctx.drawImage(tmp, x0, y0);
    L.ctx.restore();
    T.history.commitPixels(L, { x: x0, y: y0, w: bw, h: bh }, 'Relleno');
    E.pushRecent(color);
    T.view.request();
  };

  // Guías de simetría
  T.view.overlays.add((ctx) => {
    const m = tools.symmetry, d = T.app.doc;
    if (m === 'none' || !d) return;
    const V = T.view;
    ctx.strokeStyle = 'rgba(61,139,255,.75)';
    ctx.lineWidth = 1.2;
    ctx.setLineDash([6, 5]);
    const line = (ax, ay, bx, by) => {
      const a = V.toScreen(ax, ay), b = V.toScreen(bx, by);
      ctx.beginPath();
      ctx.moveTo(a.x, a.y);
      ctx.lineTo(b.x, b.y);
      ctx.stroke();
    };
    const cx = d.width / 2, cy = d.height / 2;
    if (m === 'vertical' || m === 'quadrant') line(cx, 0, cx, d.height);
    if (m === 'horizontal' || m === 'quadrant') line(0, cy, d.width, cy);
    if (m === 'radial') {
      const R = Math.hypot(cx, cy);
      for (let i = 0; i < 4; i++) {
        const t = (i * Math.PI) / 4;
        line(cx - Math.cos(t) * R, cy - Math.sin(t) * R, cx + Math.cos(t) * R, cy + Math.sin(t) * R);
      }
    }
  });
})();
