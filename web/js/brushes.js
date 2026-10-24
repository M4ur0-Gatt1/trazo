'use strict';
/* Trazo — puntas de pincel generadas por código y biblioteca de pinceles. */
(function () {
  const TS = 256; // tamaño de la punta en píxeles
  const C = TS / 2;

  function rng(seed) {
    let s = seed % 2147483647;
    return () => (s = (s * 16807) % 2147483647) / 2147483647;
  }

  function make(draw) {
    const c = T.canvas(TS, TS);
    draw(c.getContext('2d'));
    return c;
  }

  function pixels(fn) {
    return make((x) => {
      const img = x.createImageData(TS, TS);
      const d = img.data;
      for (let y = 0; y < TS; y++) {
        for (let i = 0; i < TS; i++) {
          const a = fn(i, y);
          const o = (y * TS + i) * 4;
          d[o] = d[o + 1] = d[o + 2] = 255;
          d[o + 3] = T.clamp(a, 0, 1) * 255;
        }
      }
      x.putImageData(img, 0, 0);
    });
  }

  // Ruido de valor suave (para tizas y carboncillo)
  function valueNoise(cells, seed) {
    const r = rng(seed);
    const g = [];
    for (let i = 0; i < (cells + 1) * (cells + 1); i++) g.push(r());
    return (x, y) => {
      const fx = (x / TS) * cells, fy = (y / TS) * cells;
      const ix = Math.floor(fx), iy = Math.floor(fy);
      const tx = fx - ix, ty = fy - iy;
      const sx = tx * tx * (3 - 2 * tx), sy = ty * ty * (3 - 2 * ty);
      const at = (a, b) => g[Math.min(b, cells) * (cells + 1) + Math.min(a, cells)];
      return T.lerp(T.lerp(at(ix, iy), at(ix + 1, iy), sx), T.lerp(at(ix, iy + 1), at(ix + 1, iy + 1), sx), sy);
    };
  }

  const tips = {};
  tips.round = make((x) => {
    x.fillStyle = '#fff';
    x.beginPath();
    x.arc(C, C, C - 2, 0, Math.PI * 2);
    x.fill();
  });
  tips.soft = make((x) => {
    const g = x.createRadialGradient(C, C, 0, C, C, C);
    g.addColorStop(0, 'rgba(255,255,255,1)');
    g.addColorStop(0.45, 'rgba(255,255,255,.75)');
    g.addColorStop(1, 'rgba(255,255,255,0)');
    x.fillStyle = g;
    x.fillRect(0, 0, TS, TS);
  });
  tips.airbrush = make((x) => {
    const g = x.createRadialGradient(C, C, 0, C, C, C);
    [[0, 1], [0.2, 0.82], [0.4, 0.5], [0.6, 0.22], [0.8, 0.06], [1, 0]].forEach(([o, a]) =>
      g.addColorStop(o, `rgba(255,255,255,${a})`));
    x.fillStyle = g;
    x.fillRect(0, 0, TS, TS);
  });
  {
    const r = rng(7);
    tips.pencil = pixels((i, y) => {
      const d = Math.hypot(i - C, y - C) / (C - 2);
      if (d > 1) return 0;
      const n = r();
      return Math.pow(n, 1.6) * (1 - Math.pow(d, 6)) * 1.15;
    });
  }
  {
    const coarse = valueNoise(10, 11), fine = rng(23);
    tips.chalk = pixels((i, y) => {
      const d = Math.hypot(i - C, y - C) / (C - 2);
      if (d > 1) return 0;
      const n = coarse(i, y);
      const edge = 1 - Math.pow(d, 3);
      const v = (n - 0.32) * 2.4 * edge + (fine() - 0.5) * 0.35;
      return v;
    });
  }
  tips.flat = make((x) => {
    x.fillStyle = '#fff';
    x.beginPath();
    x.ellipse(C, C, C - 2, (C - 2) * 0.22, 0, 0, Math.PI * 2);
    x.fill();
  });
  tips.spray = make((x) => {
    const r = rng(41);
    x.fillStyle = '#fff';
    for (let i = 0; i < 420; i++) {
      // distribución aproximadamente gaussiana
      const rad = Math.abs((r() + r() + r()) / 3 - 0.5) * 2 * (C - 6);
      const th = r() * Math.PI * 2;
      x.globalAlpha = 0.5 + r() * 0.5;
      x.beginPath();
      x.arc(C + Math.cos(th) * rad, C + Math.sin(th) * rad, 1.2 + r() * 2.6, 0, Math.PI * 2);
      x.fill();
    }
  });
  tips.bristle = make((x) => {
    const r = rng(97);
    x.fillStyle = '#fff';
    for (let i = 0; i < 60; i++) {
      const u = r() * 2 - 1;
      const px = C + u * (C - 14);
      const py = C + (r() * 2 - 1) * (C - 14) * 0.35 * Math.sqrt(1 - u * u);
      x.globalAlpha = 0.35 + r() * 0.55;
      x.beginPath();
      x.arc(px, py, 6 + r() * 10, 0, Math.PI * 2);
      x.fill();
    }
  });

  // ---- Biblioteca ----
  // spacing: fracción del tamaño entre sellos; minSizeP: tamaño a presión mínima;
  // flowP: cuánto afecta la presión a la opacidad; tilt: aumento de tamaño con inclinación.
  const base = {
    spacing: 0.08, size: 12, minSize: 1, maxSize: 120, minSizeP: 0.15, flow: 1, flowP: 0,
    jitter: 0, scatter: 0, rotRand: 0, rotFollow: false, angle: 0, tilt: 0, streamline: 0.3,
  };
  const B = (o) => Object.assign({}, base, o);
  const PI = Math.PI;

  T.brushCats = [
    { name: 'Bocetos', brushes: [
      B({ id: 'pencil6b', name: 'Lápiz 6B', tip: 'pencil', size: 10, maxSize: 80, spacing: 0.07, minSizeP: 0.45, flow: 0.75, flowP: 0.8, rotRand: PI, tilt: 1.6, streamline: 0.12 }),
      B({ id: 'hb', name: 'Lápiz HB', tip: 'pencil', size: 4, maxSize: 30, spacing: 0.06, minSizeP: 0.6, flow: 0.9, flowP: 0.7, rotRand: PI, tilt: 1, streamline: 0.15 }),
      B({ id: 'charcoal', name: 'Carboncillo', tip: 'chalk', size: 22, maxSize: 160, spacing: 0.1, minSizeP: 0.5, flow: 0.85, flowP: 0.7, rotRand: PI, jitter: 0.1, tilt: 1 }),
    ] },
    { name: 'Entintado', brushes: [
      B({ id: 'studiopen', name: 'Pluma de estudio', tip: 'round', size: 7, maxSize: 90, spacing: 0.04, minSizeP: 0.05, streamline: 0.55 }),
      B({ id: 'techpen', name: 'Bolígrafo técnico', tip: 'round', size: 4, maxSize: 40, spacing: 0.04, minSizeP: 0.85, streamline: 0.45 }),
      B({ id: 'dryink', name: 'Tinta seca', tip: 'chalk', size: 12, maxSize: 90, spacing: 0.05, minSizeP: 0.2, rotRand: PI, streamline: 0.35 }),
      B({ id: 'marker', name: 'Rotulador', tip: 'round', size: 16, maxSize: 120, spacing: 0.04, minSizeP: 0.9, flow: 0.55, streamline: 0.3 }),
    ] },
    { name: 'Pintura', brushes: [
      B({ id: 'roundbrush', name: 'Pincel redondo', tip: 'round', size: 30, maxSize: 300, spacing: 0.06, minSizeP: 0.3, flow: 0.65, flowP: 0.5, streamline: 0.25 }),
      B({ id: 'gouache', name: 'Gouache', tip: 'bristle', size: 44, maxSize: 320, spacing: 0.05, minSizeP: 0.45, flow: 0.55, flowP: 0.4, rotFollow: true, angle: PI / 2, streamline: 0.2 }),
      B({ id: 'oil', name: 'Óleo', tip: 'bristle', size: 56, maxSize: 400, spacing: 0.035, minSizeP: 0.6, flow: 0.9, rotFollow: true, angle: PI / 2, streamline: 0.2 }),
      B({ id: 'watercolor', name: 'Acuarela', tip: 'soft', size: 70, maxSize: 400, spacing: 0.05, minSizeP: 0.5, flow: 0.18, flowP: 0.6, jitter: 0.08, streamline: 0.25 }),
    ] },
    { name: 'Aerógrafos', brushes: [
      B({ id: 'softair', name: 'Aerógrafo suave', tip: 'airbrush', size: 140, maxSize: 900, spacing: 0.05, minSizeP: 1, flow: 0.1, flowP: 0.9, streamline: 0.1 }),
      B({ id: 'medair', name: 'Aerógrafo medio', tip: 'soft', size: 70, maxSize: 600, spacing: 0.05, minSizeP: 0.8, flow: 0.22, flowP: 0.8, streamline: 0.1 }),
      B({ id: 'hardair', name: 'Aerógrafo duro', tip: 'round', size: 40, maxSize: 500, spacing: 0.05, minSizeP: 1, flow: 0.35, flowP: 0.8, streamline: 0.1 }),
      B({ id: 'spray', name: 'Spray', tip: 'spray', size: 90, maxSize: 600, spacing: 0.18, minSizeP: 0.8, flow: 0.55, flowP: 0.6, rotRand: PI, scatter: 0.12, streamline: 0 }),
    ] },
    { name: 'Caligrafía', brushes: [
      B({ id: 'monoline', name: 'Monolínea', tip: 'round', size: 8, maxSize: 90, spacing: 0.03, minSizeP: 1, streamline: 0.65 }),
      B({ id: 'calig', name: 'Pluma caligráfica', tip: 'flat', size: 22, maxSize: 140, spacing: 0.025, minSizeP: 0.55, angle: -PI / 4, streamline: 0.55 }),
      B({ id: 'brushpen', name: 'Rotulador de pincel', tip: 'round', size: 16, maxSize: 110, spacing: 0.03, minSizeP: 0.08, streamline: 0.55 }),
    ] },
    { name: 'Texturas', brushes: [
      B({ id: 'chalk', name: 'Tiza', tip: 'chalk', size: 34, maxSize: 220, spacing: 0.14, minSizeP: 0.6, flowP: 0.5, rotRand: PI, jitter: 0.15, scatter: 0.05 }),
      B({ id: 'grain', name: 'Grano', tip: 'pencil', size: 70, maxSize: 340, spacing: 0.1, minSizeP: 0.7, flow: 0.5, flowP: 0.6, rotRand: PI }),
      B({ id: 'stipple', name: 'Punteado', tip: 'round', size: 6, maxSize: 60, spacing: 1.6, minSizeP: 0.4, jitter: 0.5, scatter: 1.2, streamline: 0 }),
    ] },
  ];

  T.brushes = {};
  T.brushDefaults = {};
  T.brushCats.forEach((cat) =>
    cat.brushes.forEach((b) => {
      b.cat = cat.name;
      T.brushes[b.id] = b;
      T.brushDefaults[b.id] = Object.assign({}, b);
    }));

  // Ajustes de pincel personalizados por el usuario
  const EDITABLE = ['streamline', 'spacing', 'flow', 'jitter', 'minSizeP', 'flowP', 'scatter'];
  T.brushEditable = EDITABLE;
  const overrides = T.settings.get('brushOverrides', {});
  for (const id in overrides) if (T.brushes[id]) Object.assign(T.brushes[id], overrides[id]);
  T.setBrushProp = (id, k, v) => {
    T.brushes[id][k] = v;
    const o = T.settings.get('brushOverrides', {});
    (o[id] || (o[id] = {}))[k] = v;
    T.settings.set('brushOverrides', o);
  };
  T.resetBrush = (id) => {
    const d = T.brushDefaults[id];
    EDITABLE.forEach((k) => (T.brushes[id][k] = d[k]));
    const o = T.settings.get('brushOverrides', {});
    delete o[id];
    T.settings.set('brushOverrides', o);
  };

  T.tips = tips;
  T.TIP = TS;

  const tintCache = new Map();
  T.tintedTip = (tip, color) => {
    const k = tip + color;
    let c = tintCache.get(k);
    if (c) return c;
    if (tintCache.size > 48) tintCache.clear();
    c = T.canvas(TS, TS);
    const x = c.getContext('2d');
    x.drawImage(tips[tip], 0, 0);
    x.globalCompositeOperation = 'source-in';
    x.fillStyle = color;
    x.fillRect(0, 0, TS, TS);
    tintCache.set(k, c);
    return c;
  };

  // Vista previa de un trazo en S para la biblioteca
  T.brushPreview = (b, w, h, color = '#f2f2f4') => {
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const c = T.canvas(w * dpr, h * dpr);
    c.style.width = w + 'px';
    c.style.height = h + 'px';
    const x = c.getContext('2d');
    const tint = T.tintedTip(b.tip, color);
    const big = Math.min(h * 0.42, Math.max(6, b.size * 0.6)) * dpr;
    let prev = null, carry = 0;
    const N = 160;
    for (let i = 0; i <= N; i++) {
      const t = i / N;
      const px = (10 + t * (w - 20)) * dpr;
      const py = (h / 2 + Math.sin(t * Math.PI * 2) * h * 0.22) * dpr;
      if (prev) {
        const dx = px - prev.x, dy = py - prev.y, len = Math.hypot(dx, dy);
        const ang = Math.atan2(dy, dx);
        let s = carry;
        while (s <= len) {
          const u = s / len;
          const p = Math.sin(T.lerp(prev.t, t, u) * Math.PI);
          let size = big * T.lerp(b.minSizeP, 1, p);
          if (b.jitter) size *= 1 + (Math.random() * 2 - 1) * b.jitter;
          size = Math.max(0.6, size);
          let qx = prev.x + dx * u, qy = prev.y + dy * u;
          if (b.scatter) {
            const r = Math.random() * b.scatter * size * 2, th = Math.random() * 6.283;
            qx += Math.cos(th) * r; qy += Math.sin(th) * r;
          }
          const rot = b.angle + (b.rotFollow ? ang : 0) + (b.rotRand ? (Math.random() * 2 - 1) * b.rotRand : 0);
          const k = size / TS, co = Math.cos(rot) * k, si = Math.sin(rot) * k;
          x.setTransform(co, si, -si, co, qx, qy);
          x.globalAlpha = Math.min(1, b.flow * T.lerp(1 - b.flowP, 1, p) * (b.flow < 0.3 ? 2 : 1));
          x.drawImage(tint, -TS / 2, -TS / 2);
          s += Math.max(0.5, size * b.spacing);
        }
        carry = s - len;
      }
      prev = { x: px, y: py, t };
    }
    x.setTransform(1, 0, 0, 1, 0, 0);
    return c;
  };
})();
