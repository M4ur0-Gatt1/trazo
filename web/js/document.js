'use strict';
/* Trazo — modelo de documento: capas, fondo y composición. */
(function () {
  const MAX_SIDE = 8192;
  const MAX_AREA = 40e6;
  T.MAX_SIDE = MAX_SIDE;
  T.MAX_AREA = MAX_AREA;

  T.BLENDS = [
    ['source-over', 'Normal', 'N'],
    ['multiply', 'Multiplicar', 'M'],
    ['darken', 'Oscurecer', 'Os'],
    ['color-burn', 'Subexponer color', 'Sc'],
    ['screen', 'Trama', 'T'],
    ['lighten', 'Aclarar', 'A'],
    ['color-dodge', 'Sobreexponer color', 'Sb'],
    ['overlay', 'Superponer', 'Su'],
    ['soft-light', 'Luz suave', 'Ls'],
    ['hard-light', 'Luz fuerte', 'Lf'],
    ['difference', 'Diferencia', 'D'],
    ['exclusion', 'Exclusión', 'E'],
    ['hue', 'Tono', 'To'],
    ['saturation', 'Saturación', 'Sa'],
    ['color', 'Color', 'C'],
    ['luminosity', 'Luminosidad', 'L'],
  ];
  T.blendInfo = (op) => T.BLENDS.find((b) => b[0] === op) || T.BLENDS[0];

  class Layer {
    constructor(w, h, name) {
      this.id = T.uid();
      this.name = name;
      this.canvas = T.canvas(w, h);
      this.ctx = this.canvas.getContext('2d');
      this.opacity = 1;
      this.blend = 'source-over';
      this.visible = true;
      this.alphaLock = false;
      this.clip = false;
      this.rev = 0; // aumenta con cada cambio de píxeles (miniaturas)
    }
    clone(doc) {
      const l = new Layer(doc.width, doc.height, this.name + ' copia');
      l.ctx.drawImage(this.canvas, 0, 0);
      ['opacity', 'blend', 'visible', 'alphaLock', 'clip'].forEach((k) => (l[k] = this[k]));
      return l;
    }
  }

  class Doc {
    constructor(w, h, name) {
      this.id = T.uid();
      this.name = name || 'Sin título';
      this.width = w;
      this.height = h;
      this.layers = []; // de abajo hacia arriba
      this.bg = { color: '#ffffff', visible: true };
      this.active = 0;
      this.created = Date.now();
      this._group = null;
    }
    get activeLayer() {
      return this.layers[this.active];
    }
    nextName() {
      const names = new Set(this.layers.map((l) => l.name));
      let n = 1;
      while (names.has('Capa ' + n)) n++;
      return 'Capa ' + n;
    }
    newLayer(name) {
      return new Layer(this.width, this.height, name || this.nextName());
    }
    insert(layer, idx) {
      this.layers.splice(idx, 0, layer);
      this.active = idx;
    }
    removeAt(idx) {
      const [l] = this.layers.splice(idx, 1);
      if (this.active > idx) this.active--;
      else if (this.active === idx) this.active = Math.max(0, idx - 1);
      this.active = T.clamp(this.active, 0, Math.max(0, this.layers.length - 1));
      return l;
    }
    maxLayers() {
      return T.clamp(Math.floor(900e6 / (this.width * this.height * 4)), 4, 200);
    }
    group() {
      if (!this._group) this._group = T.canvas(this.width, this.height);
      return this._group;
    }
    // Una capa con máscara de recorte usa la capa base inmediatamente inferior.
    isClipped(i) {
      return i > 0 && this.layers[i].clip;
    }
    composite(ctx, ov, opts = {}) {
      const W = this.width, H = this.height;
      ctx.save();
      if (this.bg.visible && !opts.noBg) {
        ctx.fillStyle = this.bg.color;
        ctx.fillRect(0, 0, W, H);
      }
      const Ls = this.layers;
      const src = (l) => (ov && ov.get(l)) || l.canvas;
      let i = 0;
      while (i < Ls.length) {
        const L = Ls[i];
        let j = i + 1;
        while (j < Ls.length && Ls[j].clip) j++;
        if (L.visible && L.opacity > 0) {
          if (j === i + 1) {
            ctx.globalAlpha = L.opacity;
            ctx.globalCompositeOperation = L.blend;
            ctx.drawImage(src(L), 0, 0);
          } else {
            const g = this.group(), gx = g.getContext('2d');
            gx.globalCompositeOperation = 'source-over';
            gx.globalAlpha = 1;
            gx.clearRect(0, 0, W, H);
            gx.drawImage(src(L), 0, 0);
            gx.globalCompositeOperation = 'source-atop';
            for (let k = i + 1; k < j; k++) {
              const Cl = Ls[k];
              if (!Cl.visible) continue;
              gx.globalAlpha = Cl.opacity;
              gx.drawImage(src(Cl), 0, 0);
            }
            gx.globalCompositeOperation = 'source-over';
            gx.globalAlpha = 1;
            ctx.globalAlpha = L.opacity;
            ctx.globalCompositeOperation = L.blend;
            ctx.drawImage(g, 0, 0);
          }
        }
        i = j;
      }
      ctx.restore();
    }
    flatten(opts) {
      const c = T.canvas(this.width, this.height);
      this.composite(c.getContext('2d'), null, opts);
      return c;
    }
  }

  T.Layer = Layer;
  T.Doc = Doc;
})();
