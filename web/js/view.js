'use strict';
/* Trazo — vista del lienzo: zoom, rotación, desplazamiento y renderizado. */
(function () {
  const V = (T.view = {
    x: 0, y: 0, scale: 1, rot: 0, dpr: 1, W: 0, H: 0,
    dirty: false,
    overrides: new Map(), // capa -> lienzo de vista previa
    overlays: new Set(), // funciones (ctx) dibujadas en coordenadas de pantalla
    MIN: 0.02, MAX: 64,
  });
  let canvas, ctx, checker;

  V.init = (c) => {
    canvas = V.canvas = c;
    ctx = V.ctx = c.getContext('2d');
    new ResizeObserver(() => V.resize()).observe(c.parentElement);
    const pc = T.canvas(16, 16), px = pc.getContext('2d');
    px.fillStyle = '#ffffff';
    px.fillRect(0, 0, 16, 16);
    px.fillStyle = '#dcdcdc';
    px.fillRect(0, 0, 8, 8);
    px.fillRect(8, 8, 8, 8);
    checker = ctx.createPattern(pc, 'repeat');
    V.resize();
  };

  V.resize = () => {
    const p = canvas.parentElement;
    const oldW = V.W, oldH = V.H;
    V.dpr = window.devicePixelRatio || 1;
    V.W = p.clientWidth;
    V.H = p.clientHeight;
    canvas.width = Math.round(V.W * V.dpr);
    canvas.height = Math.round(V.H * V.dpr);
    canvas.style.width = V.W + 'px';
    canvas.style.height = V.H + 'px';
    // Mantiene centrado el contenido al rotar la tableta
    if (oldW && T.app.doc) {
      V.x += (V.W - oldW) / 2;
      V.y += (V.H - oldH) / 2;
    }
    V.request();
  };

  V.toDoc = (sx, sy) => {
    const c = Math.cos(-V.rot), s = Math.sin(-V.rot);
    const dx = sx - V.x, dy = sy - V.y;
    return { x: (dx * c - dy * s) / V.scale, y: (dx * s + dy * c) / V.scale };
  };
  V.toScreen = (x, y) => {
    const c = Math.cos(V.rot), s = Math.sin(V.rot);
    x *= V.scale;
    y *= V.scale;
    return { x: V.x + x * c - y * s, y: V.y + x * s + y * c };
  };
  // Coloca el punto del documento (dx,dy) en la posición de pantalla (sx,sy)
  V.pin = (dx, dy, sx, sy) => {
    V.x = 0;
    V.y = 0;
    const p = V.toScreen(dx, dy);
    V.x = sx - p.x;
    V.y = sy - p.y;
  };
  V.zoomAt = (sx, sy, f) => {
    const d = V.toDoc(sx, sy);
    V.scale = T.clamp(V.scale * f, V.MIN, V.MAX);
    V.pin(d.x, d.y, sx, sy);
    V.request();
    T.emit('viewchange');
  };
  V.rotateAt = (sx, sy, da) => {
    const d = V.toDoc(sx, sy);
    V.rot += da;
    V.pin(d.x, d.y, sx, sy);
    V.request();
    T.emit('viewchange');
  };

  V.fit = () => {
    const d = T.app.doc;
    if (!d) return;
    const top = document.body.classList.contains('ui-hidden') ? 0 : 52;
    const m = 28;
    V.rot = 0;
    V.scale = T.clamp(Math.min((V.W - m * 2) / d.width, (V.H - top - m * 2) / d.height), V.MIN, V.MAX);
    V.x = (V.W - d.width * V.scale) / 2;
    V.y = top + (V.H - top - d.height * V.scale) / 2;
    V.request();
    T.emit('viewchange');
  };
  V.actualSize = () => {
    const c = V.toDoc(V.W / 2, V.H / 2);
    V.scale = 1;
    V.pin(c.x, c.y, V.W / 2, V.H / 2);
    V.request();
    T.emit('viewchange');
  };

  V.request = () => {
    if (V.dirty) return;
    V.dirty = true;
    requestAnimationFrame(render);
  };

  function render() {
    V.dirty = false;
    const d = T.app.doc;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    if (!d) return;
    T.emit('prerender');
    ctx.setTransform(V.dpr, 0, 0, V.dpr, 0, 0);
    ctx.save();
    const c = Math.cos(V.rot) * V.scale, s = Math.sin(V.rot) * V.scale;
    ctx.transform(c, s, -s, c, V.x, V.y);
    // sombra del lienzo
    ctx.save();
    ctx.shadowColor = 'rgba(0,0,0,.45)';
    ctx.shadowBlur = 22;
    ctx.fillStyle = '#000';
    ctx.fillRect(0, 0, d.width, d.height);
    ctx.restore();
    ctx.beginPath();
    ctx.rect(0, 0, d.width, d.height);
    ctx.clip();
    if (!d.bg.visible) {
      ctx.save();
      ctx.scale(1 / V.scale, 1 / V.scale);
      ctx.fillStyle = checker;
      ctx.fillRect(0, 0, d.width * V.scale, d.height * V.scale);
      ctx.restore();
    }
    ctx.imageSmoothingEnabled = V.scale < 3;
    ctx.imageSmoothingQuality = V.scale < 0.5 ? 'medium' : 'low';
    d.composite(ctx, V.overrides);
    ctx.restore();
    for (const o of V.overlays) {
      ctx.save();
      o(ctx);
      ctx.restore();
    }
  }
})();
