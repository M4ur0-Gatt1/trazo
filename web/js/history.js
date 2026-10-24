'use strict';
/* Trazo — historial de deshacer/rehacer (comandos y parches de píxeles). */
(function () {
  const H = (T.history = { undo: [], redo: [], bytes: 0, maxBytes: 600e6, maxSteps: 120 });
  let backup = null, backupCtx = null;

  const changed = () => {
    T.emit('history');
    T.emit('docchange');
  };

  H.reset = () => {
    H.undo = [];
    H.redo = [];
    H.bytes = 0;
    backup = null;
    backupCtx = null;
    T.emit('history');
  };

  H.push = (cmd) => {
    H.redo.forEach((c) => (H.bytes -= c.bytes || 0));
    H.redo = [];
    H.undo.push(cmd);
    H.bytes += cmd.bytes || 0;
    while (H.undo.length > H.maxSteps || (H.bytes > H.maxBytes && H.undo.length > 1)) {
      const c = H.undo.shift();
      H.bytes -= c.bytes || 0;
    }
    changed();
  };

  // Ejecuta un comando reversible y lo guarda: {label, do(), undo(), bytes}
  H.run = (cmd) => {
    cmd.do();
    cmd.redo = cmd.redo || cmd.do;
    H.push(cmd);
  };

  H.doUndo = () => {
    const c = H.undo.pop();
    if (!c) return null;
    c.undo();
    H.redo.push(c);
    changed();
    return c;
  };
  H.doRedo = () => {
    const c = H.redo.pop();
    if (!c) return null;
    c.redo();
    H.undo.push(c);
    changed();
    return c;
  };

  // Copia la capa antes de una operación de píxeles
  H.snapshot = (layer) => {
    const d = T.app.doc;
    if (!backup || backup.width !== d.width || backup.height !== d.height) {
      backup = T.canvas(d.width, d.height);
      backupCtx = backup.getContext('2d');
    }
    backupCtx.globalCompositeOperation = 'copy';
    backupCtx.drawImage(layer.canvas, 0, 0);
    backupCtx.globalCompositeOperation = 'source-over';
    return backup;
  };
  H.backupCanvas = () => backup;

  H.clipRect = (r) => {
    const d = T.app.doc;
    const x = Math.max(0, Math.floor(r.x)), y = Math.max(0, Math.floor(r.y));
    const x2 = Math.min(d.width, Math.ceil(r.x + r.w)), y2 = Math.min(d.height, Math.ceil(r.y + r.h));
    if (x2 <= x || y2 <= y) return null;
    return { x, y, w: x2 - x, h: y2 - y };
  };

  // Registra el cambio entre la copia previa (snapshot) y el estado actual dentro de rect
  H.commitPixels = (layer, rect, label) => {
    const r = rect ? H.clipRect(rect) : { x: 0, y: 0, w: T.app.doc.width, h: T.app.doc.height };
    layer.rev++;
    if (!r || !backupCtx) return;
    const before = backupCtx.getImageData(r.x, r.y, r.w, r.h);
    const after = layer.ctx.getImageData(r.x, r.y, r.w, r.h);
    H.push({
      label: label || 'Trazo',
      bytes: before.data.length * 2,
      undo() { layer.ctx.putImageData(before, r.x, r.y); layer.rev++; },
      redo() { layer.ctx.putImageData(after, r.x, r.y); layer.rev++; },
    });
  };

  // Atajo: aplica fn(ctx) sobre una capa completa con historial
  H.pixelOp = (layer, label, fn) => {
    H.snapshot(layer);
    fn(layer.ctx, layer);
    H.commitPixels(layer, null, label);
  };
})();
