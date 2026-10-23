'use strict';
/* Trazo — entrada: lápiz (presión, inclinación, botón lateral, borrador),
   gestos multitáctiles estilo Procreate, ratón, rueda y teclado. */
(function () {
  const I = (T.input = { penOnly: T.settings.get('penOnly', false), penSeen: false, blocked: false, mode: 'idle' });
  const V = T.view;
  const ptrs = new Map();
  let canvas;
  let stroke = null, strokeId = null, strokeType = null, strokeT0 = 0, toolBeforeOverride = null;
  let holdTimer = null, shapeTimer = null, shapeAnchor = null;
  let gesture = null, eyedrop = null, pan = null;
  let spaceDown = false, altDown = false;
  let hover = null;
  I.eyedropOnce = false;
  I.fillOnce = false;

  const setMode = (m) => {
    I.mode = m;
    T.emit('inputmode', m);
  };
  const local = (e) => {
    const r = canvas.getBoundingClientRect();
    return { sx: e.clientX - r.left, sy: e.clientY - r.top };
  };
  const touches = () => [...ptrs.values()].filter((p) => p.type === 'touch');

  function docPoint(e) {
    const s = local(e);
    const d = V.toDoc(s.sx, s.sy);
    let p = 1, tilt = 0;
    if (e.pointerType === 'pen') {
      p = Math.max(0.02, e.pressure || 0);
      p = Math.pow(p, T.settings.get('pressureGamma', 1));
      if (e.tiltX || e.tiltY) tilt = Math.min(1, Math.hypot(e.tiltX, e.tiltY) / 70);
    }
    return { x: d.x, y: d.y, p, tilt, sx: s.sx, sy: s.sy };
  }

  I.setPenOnly = (v) => {
    I.penOnly = v;
    T.settings.set('penOnly', v);
  };

  // ---------- Dibujo ----------
  function startDraw(e, override) {
    if (I.blocked) return;
    if (override && override !== T.tools.current) {
      toolBeforeOverride = T.tools.current;
      T.engine.setTool(override, true);
    }
    const p = docPoint(e);
    stroke = T.engine.begin(p);
    if (!stroke) {
      restoreTool();
      return;
    }
    setMode('draw');
    strokeId = e.pointerId;
    strokeType = e.pointerType;
    strokeT0 = performance.now();
    shapeAnchor = { sx: p.sx, sy: p.sy };
    armShape();
    if (e.pointerType === 'touch') {
      const P = ptrs.get(e.pointerId);
      clearTimeout(holdTimer);
      holdTimer = setTimeout(() => {
        // tocar y mantener sin moverse = cuentagotas
        if (I.mode === 'draw' && stroke && P && P.moved < 10) {
          stroke.cancel();
          stroke = null;
          restoreTool();
          startEyedrop(P.id, local(P.last));
        }
      }, 450);
    }
  }
  function restoreTool() {
    if (toolBeforeOverride) {
      T.engine.setTool(toolBeforeOverride, true);
      toolBeforeOverride = null;
    }
  }
  function armShape() {
    clearTimeout(shapeTimer);
    if (!T.settings.get('quickShape', true)) return;
    shapeTimer = setTimeout(() => {
      if (I.mode === 'draw' && stroke && !stroke.shape && performance.now() - strokeT0 > 500) {
        if (stroke.snap()) clearTimeout(holdTimer);
      }
    }, 600);
  }
  function endDraw(cancel) {
    clearTimeout(holdTimer);
    clearTimeout(shapeTimer);
    if (stroke) cancel ? stroke.cancel() : stroke.end();
    stroke = null;
    strokeId = null;
    restoreTool();
    setMode('idle');
  }

  // ---------- Cuentagotas ----------
  function startEyedrop(id, s) {
    setMode('eyedrop');
    eyedrop = { id, old: T.tools.color, cur: null, sx: s.sx, sy: s.sy, touch: ptrs.get(id) && ptrs.get(id).type === 'touch' };
    V.overlays.add(drawLoupe);
    sampleEyedrop(s);
  }
  function sampleEyedrop(s) {
    eyedrop.sx = s.sx;
    eyedrop.sy = s.sy;
    const d = V.toDoc(s.sx, s.sy);
    const c = T.engine.sample(d.x, d.y);
    if (c) eyedrop.cur = c;
    V.request();
  }
  function endEyedrop(apply) {
    V.overlays.delete(drawLoupe);
    if (apply && eyedrop && eyedrop.cur) T.engine.setColor(eyedrop.cur);
    eyedrop = null;
    I.eyedropOnce = false;
    T.emit('eyedropmode', false);
    setMode('idle');
    V.request();
  }
  function drawLoupe(ctx) {
    if (!eyedrop) return;
    const x = eyedrop.sx, y = eyedrop.sy - (eyedrop.touch ? 90 : 0);
    const R = 46;
    ctx.lineWidth = 16;
    ctx.beginPath();
    ctx.arc(x, y, R, Math.PI, 0);
    ctx.strokeStyle = eyedrop.cur || eyedrop.old;
    ctx.stroke();
    ctx.beginPath();
    ctx.arc(x, y, R, 0, Math.PI);
    ctx.strokeStyle = eyedrop.old;
    ctx.stroke();
    ctx.lineWidth = 1.5;
    ctx.strokeStyle = 'rgba(255,255,255,.9)';
    ctx.beginPath();
    ctx.arc(x, y, R + 8, 0, Math.PI * 2);
    ctx.stroke();
    ctx.beginPath();
    ctx.arc(x, y, R - 8, 0, Math.PI * 2);
    ctx.stroke();
  }

  // ---------- Gestos ----------
  function startGesture() {
    const ts = touches();
    if (!gesture) {
      gesture = { t0: Math.min(...ts.map((t) => t.t0)), max: 0, moved: 0, s0: V.scale, rotOn: false };
    }
    setMode('gesture');
    anchorGesture();
  }
  function anchorGesture() {
    const ts = touches();
    gesture.max = Math.max(gesture.max, ts.length);
    if (!ts.length) return;
    const a = local(ts[0].last), b = ts[1] ? local(ts[1].last) : null;
    const cx = b ? (a.sx + b.sx) / 2 : a.sx, cy = b ? (a.sy + b.sy) / 2 : a.sy;
    gesture.a = {
      ids: ts.slice(0, 2).map((t) => t.id), cx, cy,
      d: b ? Math.hypot(b.sx - a.sx, b.sy - a.sy) : 0,
      ang: b ? Math.atan2(b.sy - a.sy, b.sx - a.sx) : 0,
      scale: V.scale, rot: V.rot, doc: V.toDoc(cx, cy),
    };
  }
  function moveGesture() {
    const g = gesture.a;
    const P = g.ids.map((id) => ptrs.get(id)).filter(Boolean);
    if (!P.length) return;
    const a = local(P[0].last), b = P[1] ? local(P[1].last) : null;
    const cx = b ? (a.sx + b.sx) / 2 : a.sx, cy = b ? (a.sy + b.sy) / 2 : a.sy;
    if (b && g.d > 0) {
      const d = Math.hypot(b.sx - a.sx, b.sy - a.sy);
      V.scale = T.clamp((g.scale * d) / g.d, V.MIN, V.MAX);
      if (T.settings.get('rotateGesture', true)) {
        let da = Math.atan2(b.sy - a.sy, b.sx - a.sx) - g.ang;
        da = Math.atan2(Math.sin(da), Math.cos(da));
        if (!gesture.rotOn && Math.abs(da) > 0.2) {
          gesture.rotOn = true;
          g.ang += da; // evita un salto al activar la rotación
          da = 0;
        }
        if (gesture.rotOn) V.rot = g.rot + da;
      }
    }
    V.pin(g.doc.x, g.doc.y, cx, cy);
    V.request();
    T.emit('viewchange');
  }
  function endGesture() {
    const g = gesture;
    gesture = null;
    setMode('idle');
    if (!g) return;
    const dur = performance.now() - g.t0;
    if (dur < 320 && g.moved < 18) {
      if (g.max === 2) T.app.undo();
      else if (g.max === 3) T.app.redo();
      else if (g.max >= 4) T.ui.toggleFullscreen();
      return;
    }
    if (dur < 450 && V.scale < g.s0 * 0.62) {
      V.fit(); // pellizco rápido = ajustar a la pantalla
      return;
    }
    // ajusta la rotación a múltiplos de 90° si está cerca
    const deg = (V.rot * 180) / Math.PI;
    const near = Math.round(deg / 90) * 90;
    if (Math.abs(deg - near) < 7 && deg !== near) V.rotateAt(V.W / 2, V.H / 2, ((near - deg) * Math.PI) / 180);
  }

  // ---------- Eventos de puntero ----------
  function onDown(e) {
    if (!T.app.doc) return;
    e.preventDefault();
    try { canvas.setPointerCapture(e.pointerId); } catch (err) { /* puntero ya liberado */ }
    const P = { id: e.pointerId, type: e.pointerType, x0: e.clientX, y0: e.clientY, t0: performance.now(), moved: 0, last: e };
    ptrs.set(e.pointerId, P);
    hover = null;

    if (T.transform.active) return T.transform.down(e, ptrs);

    if (e.pointerType === 'pen') {
      if (!I.penSeen) {
        I.penSeen = true;
        if (!T.settings.has('penOnly')) {
          I.setPenOnly(true);
          T.toast('Lápiz detectado: ahora el dedo navega y el lápiz dibuja. Cámbialo en Acciones › Preferencias.', 3800);
        }
      }
      if (I.mode === 'gesture') { gesture && endGesture(); }
      if (I.mode === 'draw' && strokeType === 'touch') endDraw(true);
      if (I.mode !== 'idle') return;
      if (I.eyedropOnce || (e.buttons & 2)) return startEyedrop(e.pointerId, local(e));
      if (I.fillOnce) return doFill(e);
      return startDraw(e, e.buttons & 32 || e.button === 5 ? 'eraser' : null);
    }

    if (e.pointerType === 'mouse') {
      if (I.mode !== 'idle') return;
      if (e.button === 1 || (e.button === 0 && spaceDown)) {
        pan = { id: e.pointerId, sx: e.clientX, sy: e.clientY, x: V.x, y: V.y };
        canvas.style.cursor = 'grabbing';
        return setMode('pan');
      }
      if (e.button === 2 || (e.button === 0 && (altDown || I.eyedropOnce))) return startEyedrop(e.pointerId, local(e));
      if (e.button === 0) {
        if (I.fillOnce) return doFill(e);
        startDraw(e);
      }
      return;
    }

    // táctil
    const ts = touches();
    if (I.mode === 'draw' && strokeType === 'pen') return; // rechazo de palma
    if (I.mode === 'eyedrop') {
      if (ts.length >= 2) endEyedrop(false);
      else return;
    }
    if (I.mode === 'draw' && strokeType === 'touch') {
      // un segundo dedo al inicio del trazo lo convierte en gesto
      if (performance.now() - strokeT0 < 280 || (stroke && stroke.raw.length < 10)) endDraw(true);
      else return;
    }
    if (I.mode === 'gesture') return anchorGesture();
    if (ts.length === 1) {
      if (I.eyedropOnce) return startEyedrop(e.pointerId, local(e));
      if (I.fillOnce) return doFill(e);
      if (I.penOnly || I.blocked) return startGesture();
      return startDraw(e);
    }
    startGesture();
  }

  function onMove(e) {
    const P = ptrs.get(e.pointerId);
    if (!P) {
      if (e.pointerType !== 'touch' && T.app.doc) {
        hover = local(e);
        if (T.settings.get('brushCursor', true)) V.request();
      }
      return;
    }
    P.moved = Math.max(P.moved, Math.hypot(e.clientX - P.x0, e.clientY - P.y0));
    P.last = e;
    if (T.transform.active) return T.transform.move(e, ptrs);
    switch (I.mode) {
      case 'draw': {
        if (e.pointerId !== strokeId || !stroke) return;
        const evs = e.getCoalescedEvents ? e.getCoalescedEvents() : [];
        for (const ce of evs.length ? evs : [e]) stroke.add(docPoint(ce));
        const s = local(e);
        if (Math.hypot(s.sx - shapeAnchor.sx, s.sy - shapeAnchor.sy) > 4) {
          shapeAnchor = s;
          if (!stroke.shape) armShape();
        }
        if (P.moved > 10) clearTimeout(holdTimer);
        if (e.pointerType !== 'touch') hover = s;
        break;
      }
      case 'gesture':
        if (P.type !== 'touch') return;
        gesture.moved = Math.max(gesture.moved, P.moved);
        moveGesture();
        break;
      case 'eyedrop':
        if (eyedrop && e.pointerId === eyedrop.id) sampleEyedrop(local(e));
        break;
      case 'pan':
        if (pan && e.pointerId === pan.id) {
          V.x = pan.x + e.clientX - pan.sx;
          V.y = pan.y + e.clientY - pan.sy;
          V.request();
        }
        break;
    }
  }

  function onUp(e, cancelled) {
    const P = ptrs.get(e.pointerId);
    ptrs.delete(e.pointerId);
    if (!P) return;
    if (T.transform.active) return T.transform.up(e, ptrs);
    switch (I.mode) {
      case 'draw':
        if (e.pointerId === strokeId) endDraw(cancelled);
        break;
      case 'eyedrop':
        if (eyedrop && e.pointerId === eyedrop.id) endEyedrop(!cancelled);
        break;
      case 'pan':
        if (pan && e.pointerId === pan.id) {
          pan = null;
          canvas.style.cursor = '';
          setMode('idle');
        }
        break;
      case 'gesture':
        if (!touches().length) endGesture();
        else anchorGesture();
        break;
    }
  }

  function doFill(e) {
    const d = docPoint(e);
    I.fillOnce = false;
    T.emit('fillmode', false);
    T.engine.fill(d.x, d.y, T.tools.color);
  }
  I.fillAtClient = (cx, cy) => {
    const r = canvas.getBoundingClientRect();
    const d = V.toDoc(cx - r.left, cy - r.top);
    T.engine.fill(d.x, d.y, T.tools.color);
  };

  // Cursor del pincel (ratón y lápiz flotando)
  function drawCursor(ctx) {
    if (!hover || !T.app.doc || T.transform.active || !T.settings.get('brushCursor', true)) return;
    if (I.mode !== 'idle' && I.mode !== 'draw') return;
    const st = T.engine.state();
    const r = Math.max(2, (st.size * V.scale) / 2);
    ctx.lineWidth = 1;
    ctx.strokeStyle = 'rgba(0,0,0,.55)';
    ctx.beginPath();
    ctx.arc(hover.sx, hover.sy, r + 0.75, 0, Math.PI * 2);
    ctx.stroke();
    ctx.strokeStyle = 'rgba(255,255,255,.85)';
    ctx.beginPath();
    ctx.arc(hover.sx, hover.sy, r, 0, Math.PI * 2);
    ctx.stroke();
  }

  // ---------- Rueda / panel táctil ----------
  function onWheel(e) {
    if (!T.app.doc) return;
    e.preventDefault();
    if (T.transform.active && !e.ctrlKey) return T.transform.wheel(e);
    const s = local(e);
    const mouseNotch = e.deltaMode !== 0 || (Math.abs(e.deltaY) >= 50 && e.deltaX === 0);
    if (e.ctrlKey || e.metaKey) V.zoomAt(s.sx, s.sy, Math.exp(-e.deltaY * 0.01));
    else if (e.altKey) V.rotateAt(s.sx, s.sy, (e.deltaY > 0 ? 1 : -1) * (Math.PI / 36));
    else if (e.shiftKey) { V.x -= e.deltaY || e.deltaX; V.request(); }
    else if (mouseNotch) V.zoomAt(s.sx, s.sy, e.deltaY > 0 ? 1 / 1.15 : 1.15);
    else { V.x -= e.deltaX; V.y -= e.deltaY; V.request(); }
  }

  // ---------- Teclado ----------
  function onKey(e) {
    if (!T.app.doc || !T.app.inStudio()) return;
    if (e.target.closest && e.target.closest('input,textarea,select,[contenteditable]')) return;
    const k = e.key.toLowerCase();
    const mod = e.ctrlKey || e.metaKey;
    if (k === 'alt') { altDown = true; e.preventDefault(); return; }
    if (mod) {
      if (k === 'z') { e.preventDefault(); e.shiftKey ? T.app.redo() : T.app.undo(); }
      else if (k === 'y') { e.preventDefault(); T.app.redo(); }
      else if (k === 's') { e.preventDefault(); T.app.save().then(() => T.toast('Guardado')); }
      else if (k === '0') { e.preventDefault(); V.fit(); }
      else if (k === '1') { e.preventDefault(); V.actualSize(); }
      else if (k === 'j') { e.preventDefault(); T.layers.duplicate(T.app.doc.active); }
      else if (k === 'e') { e.preventDefault(); T.layers.mergeDown(T.app.doc.active); }
      else if (k === '=' || k === '+') { e.preventDefault(); V.zoomAt(V.W / 2, V.H / 2, 1.25); }
      else if (k === '-') { e.preventDefault(); V.zoomAt(V.W / 2, V.H / 2, 0.8); }
      return;
    }
    if (T.transform.active) {
      if (k === 'enter') T.transform.apply();
      else if (k === 'escape') T.transform.cancel();
      return;
    }
    switch (k) {
      case ' ': spaceDown = true; canvas.style.cursor = 'grab'; e.preventDefault(); break;
      case 'b': T.engine.setTool('brush'); break;
      case 'e': T.engine.setTool('eraser'); break;
      case 's': T.engine.setTool('smudge'); break;
      case 'i': I.eyedropOnce = true; T.emit('eyedropmode', true); break;
      case 'g': I.fillOnce = true; T.emit('fillmode', true); T.toast('Toca el lienzo para rellenar'); break;
      case 'v': T.transform.start(); break;
      case 'l': T.ui.openLayers(); break;
      case 'c': T.ui.openColor(); break;
      case '[': T.engine.setSize(T.engine.state().size / 1.15); break;
      case ']': T.engine.setSize(T.engine.state().size * 1.15); break;
      case '0': V.fit(); break;
      case 'r': V.rotateAt(V.W / 2, V.H / 2, -V.rot); break;
      case 'x': { const c = T.tools.color; T.engine.setColor(T.tools.prevColor); T.tools.prevColor = c; break; }
      case 'tab': e.preventDefault(); T.ui.toggleFullscreen(); break;
      case 'escape': T.ui.closePopover(); break;
      case 'delete': case 'backspace': T.layers.clear(T.app.doc.active); break;
      default:
        if (/^[1-9]$/.test(k)) T.engine.setOpacity(Number(k) / 10);
    }
  }
  function onKeyUp(e) {
    const k = e.key.toLowerCase();
    if (k === ' ') { spaceDown = false; if (I.mode !== 'pan') canvas.style.cursor = ''; }
    if (k === 'alt') altDown = false;
  }

  I.init = (c) => {
    canvas = c;
    c.addEventListener('pointerdown', onDown);
    c.addEventListener('pointermove', onMove);
    c.addEventListener('pointerup', (e) => onUp(e, false));
    c.addEventListener('pointercancel', (e) => onUp(e, true));
    c.addEventListener('pointerleave', (e) => { if (e.pointerType !== 'touch' && !ptrs.has(e.pointerId)) { hover = null; V.request(); } });
    c.addEventListener('wheel', onWheel, { passive: false });
    c.addEventListener('contextmenu', (e) => e.preventDefault());
    window.addEventListener('keydown', onKey);
    window.addEventListener('keyup', onKeyUp);
    window.addEventListener('blur', () => { spaceDown = altDown = false; });
    V.overlays.add(drawCursor);
  };
  I.cancelAll = () => {
    if (I.mode === 'draw') endDraw(true);
    if (I.mode === 'eyedrop') endEyedrop(false);
    gesture = null;
    pan = null;
    ptrs.clear();
    setMode('idle');
  };
})();
