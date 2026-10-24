'use strict';
/* Trazo — operaciones de capa con deshacer. */
(function () {
  const doc = () => T.app.doc;
  const H = T.history;
  const copyOf = (c) => {
    const k = T.canvas(c.width, c.height);
    k.getContext('2d').drawImage(c, 0, 0);
    return k;
  };
  const restore = (layer, from) => {
    layer.ctx.save();
    layer.ctx.globalCompositeOperation = 'copy';
    layer.ctx.drawImage(from, 0, 0);
    layer.ctx.restore();
    layer.rev++;
  };
  const layerBytes = () => doc().width * doc().height * 4;

  const L = (T.layers = {
    select(i) {
      const d = doc();
      d.active = T.clamp(i, 0, d.layers.length - 1);
      T.emit('layers');
    },

    add(layer, at) {
      const d = doc();
      if (d.layers.length >= d.maxLayers()) {
        T.toast(`Límite de ${d.maxLayers()} capas para este tamaño de lienzo`);
        return null;
      }
      const l = layer || d.newLayer();
      const idx = at == null ? d.active + 1 : at;
      const prev = d.active;
      H.run({
        label: 'Nueva capa',
        bytes: layerBytes(),
        do() { d.insert(l, idx); },
        undo() { d.layers.splice(d.layers.indexOf(l), 1); d.active = T.clamp(prev, 0, d.layers.length - 1); },
      });
      T.emit('layers');
      return l;
    },

    remove(i) {
      const d = doc();
      if (d.layers.length <= 1) {
        L.clear(i);
        T.toast('Se vació la única capa');
        return;
      }
      const l = d.layers[i];
      const prev = d.active;
      H.run({
        label: 'Eliminar capa',
        do() { d.removeAt(d.layers.indexOf(l)); },
        undo() { d.layers.splice(i, 0, l); d.active = prev; },
      });
      T.emit('layers');
    },

    duplicate(i) {
      const d = doc();
      L.add(d.layers[i].clone(d), i + 1);
    },

    move(from, to) {
      const d = doc();
      if (from === to || to < 0 || to >= d.layers.length) return;
      const l = d.layers[from];
      H.run({
        label: 'Mover capa',
        do() { d.layers.splice(from, 1); d.layers.splice(to, 0, l); d.active = to; },
        undo() { d.layers.splice(to, 1); d.layers.splice(from, 0, l); d.active = from; },
      });
      T.emit('layers');
    },

    // Cambia una propiedad; los cambios rápidos consecutivos (sliders) se agrupan
    set(layer, key, value, label) {
      const old = layer[key];
      if (old === value) return;
      const top = H.undo[H.undo.length - 1];
      if (top && top.prop && top.layer === layer && top.key === key && Date.now() - top.t < 900) {
        top.value = value;
        top.t = Date.now();
        layer[key] = value;
        T.emit('docchange');
        T.emit('layers');
        return;
      }
      const cmd = {
        label: label || 'Propiedad de capa', prop: true, layer, key, value, t: Date.now(),
        do() { layer[key] = cmd.value; },
        undo() { layer[key] = old; },
      };
      H.run(cmd);
      T.emit('layers');
    },

    setBg(key, value) {
      const d = doc();
      const old = d.bg[key];
      if (old === value) return;
      const top = H.undo[H.undo.length - 1];
      if (top && top.bgKey === key && Date.now() - top.t < 900) {
        top.value = value;
        top.t = Date.now();
        d.bg[key] = value;
        T.emit('docchange');
        T.emit('layers');
        return;
      }
      const cmd = {
        label: 'Fondo', bgKey: key, value, t: Date.now(),
        do() { d.bg[key] = cmd.value; },
        undo() { d.bg[key] = old; },
      };
      H.run(cmd);
      T.emit('layers');
    },

    rename(i, name) {
      L.set(doc().layers[i], 'name', name, 'Renombrar capa');
    },

    clear(i) {
      const l = doc().layers[i];
      H.pixelOp(l, 'Borrar capa', (c) => c.clearRect(0, 0, l.canvas.width, l.canvas.height));
      T.emit('layers');
    },

    fill(i, color) {
      const l = doc().layers[i];
      H.pixelOp(l, 'Rellenar capa', (c) => {
        c.save();
        c.fillStyle = color;
        if (l.alphaLock) c.globalCompositeOperation = 'source-atop';
        c.fillRect(0, 0, l.canvas.width, l.canvas.height);
        c.restore();
      });
      T.emit('layers');
    },

    invert(i) {
      const l = doc().layers[i];
      H.pixelOp(l, 'Invertir', (c) => {
        const k = copyOf(l.canvas);
        c.save();
        c.globalCompositeOperation = 'copy';
        c.filter = 'invert(100%)';
        c.drawImage(k, 0, 0);
        c.restore();
      });
      T.emit('layers');
    },

    mergeDown(i) {
      const d = doc();
      if (i <= 0) return;
      const up = d.layers[i], dn = d.layers[i - 1];
      const before = copyOf(dn.canvas);
      let after = null;
      const prev = d.active;
      H.run({
        label: 'Combinar abajo',
        bytes: layerBytes() * 2,
        do() {
          if (!after) {
            if (up.visible) {
              dn.ctx.save();
              dn.ctx.globalAlpha = up.opacity;
              dn.ctx.globalCompositeOperation = up.clip ? 'source-atop' : up.blend;
              dn.ctx.drawImage(up.canvas, 0, 0);
              dn.ctx.restore();
            }
            after = copyOf(dn.canvas);
          } else restore(dn, after);
          dn.rev++;
          d.layers.splice(d.layers.indexOf(up), 1);
          d.active = d.layers.indexOf(dn);
        },
        undo() {
          restore(dn, before);
          d.layers.splice(d.layers.indexOf(dn) + 1, 0, up);
          d.active = prev;
        },
      });
      T.emit('layers');
    },

    // Voltea todo el lienzo (es su propio inverso)
    flipCanvas(horizontal) {
      const d = doc();
      const flip = () => {
        d.layers.forEach((l) => {
          const k = copyOf(l.canvas);
          l.ctx.save();
          l.ctx.globalCompositeOperation = 'copy';
          if (horizontal) l.ctx.setTransform(-1, 0, 0, 1, d.width, 0);
          else l.ctx.setTransform(1, 0, 0, -1, 0, d.height);
          l.ctx.drawImage(k, 0, 0);
          l.ctx.restore();
          l.rev++;
        });
      };
      H.run({ label: 'Voltear lienzo', do: flip, undo: flip });
      T.emit('layers');
    },

    // Inserta una imagen como nueva capa, ajustada y centrada
    addImage(img, name) {
      const d = doc();
      const l = d.newLayer(name || 'Imagen');
      const iw = img.width || img.naturalWidth, ih = img.height || img.naturalHeight;
      const k = Math.min(1, d.width / iw, d.height / ih);
      const w = iw * k, h = ih * k;
      l.ctx.imageSmoothingQuality = 'high';
      l.ctx.drawImage(img, (d.width - w) / 2, (d.height - h) / 2, w, h);
      return L.add(l);
    },
  });

  // Recorta los cuadros vacíos y devuelve el rectángulo con contenido de una capa
  T.contentBounds = (canvas) => {
    const w = canvas.width, h = canvas.height;
    const data = canvas.getContext('2d').getImageData(0, 0, w, h).data;
    let x0 = w, y0 = h, x1 = -1, y1 = -1;
    for (let y = 0; y < h; y++) {
      let row = y * w * 4 + 3;
      for (let x = 0; x < w; x++, row += 4) {
        if (data[row]) {
          if (x < x0) x0 = x;
          if (x > x1) x1 = x;
          if (y < y0) y0 = y;
          y1 = y;
        }
      }
    }
    return x1 < 0 ? null : { x: x0, y: y0, w: x1 - x0 + 1, h: y1 - y0 + 1 };
  };
})();
