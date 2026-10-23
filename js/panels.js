'use strict';
/* Trazo — paneles: biblioteca de pinceles, capas y color. */
(function () {
  const Pn = (T.panels = {});
  const el = T.el, U = T.ui;

  // ================= Biblioteca de pinceles =================
  const TOOL_NAMES = { brush: 'Pintar', smudge: 'Difuminar', eraser: 'Borrar' };
  Pn.brushLibrary = (anchor, tool) => {
    U.popover(anchor, (box) => {
      const cur = T.tools[tool].brush;
      let cat = T.brushes[cur].cat;
      const cats = el('div', { class: 'bl-cats scroll' });
      const list = el('div', { class: 'bl-list scroll' });
      const settings = el('div', { class: 'bl-settings' });

      const renderCats = () => {
        cats.innerHTML = '';
        T.brushCats.forEach((c) => cats.append(el('button', { class: c.name === cat ? 'on' : '', text: c.name, onclick: () => { cat = c.name; renderCats(); renderList(); } })));
      };
      const renderList = () => {
        list.innerHTML = '';
        T.brushCats.find((c) => c.name === cat).brushes.forEach((b) => {
          const on = b.id === T.tools[tool].brush;
          list.append(el('button', { class: 'bl-item' + (on ? ' on' : ''), onclick: () => { T.engine.setBrush(tool, b.id); renderList(); renderSettings(); } },
            el('span', { class: 'bl-name', text: b.name }),
            T.brushPreview(b, 200, 44, on ? '#ffffff' : '#c9c9cf')));
        });
      };
      const renderSettings = () => {
        const b = T.brushes[T.tools[tool].brush];
        settings.innerHTML = '';
        const rerender = T.debounce(renderList, 120);
        const set = (k) => (v) => { T.setBrushProp(b.id, k, v); rerender(); };
        settings.append(
          el('div', { class: 'menu-label', text: 'Ajustes de «' + b.name + '»' }),
          U.slider('StreamLine', 0, 1, b.streamline, '', set('streamline'), 0.01),
          U.slider('Espaciado', 0.01, 2, b.spacing, '', set('spacing'), 0.01),
          U.slider('Flujo', 0.02, 1, b.flow, '', set('flow'), 0.01),
          U.slider('Tamaño a presión mínima', 0, 1, b.minSizeP, '', set('minSizeP'), 0.01),
          U.slider('Opacidad por presión', 0, 1, b.flowP, '', set('flowP'), 0.01),
          U.slider('Variación de tamaño', 0, 1, b.jitter, '', set('jitter'), 0.01),
          U.slider('Dispersión', 0, 2, b.scatter, '', set('scatter'), 0.01),
          el('button', { class: 'pill small', text: 'Restablecer pincel', onclick: () => { T.resetBrush(b.id); renderSettings(); renderList(); } }));
      };
      const showSettings = () => {
        box.classList.toggle('with-settings');
      };
      box.append(
        el('div', { class: 'pop-head' }, el('h3', { text: 'Biblioteca de pinceles · ' + TOOL_NAMES[tool] }), el('div', { class: 'grow' }),
          el('button', { class: 'icon-btn', title: 'Ajustes del pincel', 'data-icon': 'prefs', onclick: showSettings })),
        el('div', { class: 'bl-body' }, cats, list),
        settings);
      renderCats();
      renderList();
      renderSettings();
    }, { width: 440, cls: 'brush-lib' });
  };

  // ================= Capas =================
  const thumbs = new WeakMap(); // capa -> {canvas, rev}
  function thumbFor(layer, d) {
    let t = thumbs.get(layer);
    const tw = 64, th = Math.max(24, Math.round((64 * d.height) / d.width));
    if (!t) {
      const c = T.canvas(tw * 2, Math.min(96, th) * 2);
      t = { canvas: c, rev: -1 };
      thumbs.set(layer, t);
    }
    if (t.rev !== layer.rev) {
      const x = t.canvas.getContext('2d');
      x.clearRect(0, 0, t.canvas.width, t.canvas.height);
      const k = Math.min(t.canvas.width / d.width, t.canvas.height / d.height);
      x.drawImage(layer.canvas, (t.canvas.width - d.width * k) / 2, (t.canvas.height - d.height * k) / 2, d.width * k, d.height * k);
      t.rev = layer.rev;
    }
    return t.canvas;
  }

  let layersRefresh = null;
  Pn.layers = (anchor) => {
    const p = U.popover(anchor, (box) => {
      const st = U.stack(box);
      const list = () => {
        const d = T.app.doc;
        const wrap = el('div', { class: 'layer-list scroll' });
        for (let i = d.layers.length - 1; i >= 0; i--) wrap.append(layerRow(d, i, st));
        wrap.append(bgRow(d));
        return wrap;
      };
      st.push('Capas', list, el('button', { class: 'icon-btn', 'data-icon': 'plus', title: 'Nueva capa', onclick: () => T.layers.add() }));
      layersRefresh = () => { if (st.depth() === 1) st.refresh(); };
    }, { width: 340, onClose: () => (layersRefresh = null) });
    return p;
  };
  const refreshSoon = T.debounce(() => layersRefresh && layersRefresh(), 120);
  T.on('layers', () => layersRefresh && layersRefresh());
  T.on('history', refreshSoon);

  function layerRow(d, i, st) {
    const L = d.layers[i];
    const active = i === d.active;
    const thumb = el('div', { class: 'lr-thumb' + (L.clip && i > 0 ? ' clipped' : '') });
    const tc = thumbFor(L, d);
    const img = T.canvas(tc.width, tc.height);
    img.getContext('2d').drawImage(tc, 0, 0);
    thumb.append(img);
    const info = T.blendInfo(L.blend);
    const row = el('div', { class: 'layer-row' + (active ? ' active' : '') + (L.visible ? '' : ' hidden-layer') },
      L.clip && i > 0 ? el('span', { class: 'clip-mark', 'data-icon': 'clip' }) : null,
      thumb,
      el('div', { class: 'lr-name' },
        el('span', { text: L.name }),
        el('small', {}, L.alphaLock ? el('span', { class: 'tag', text: 'α' }) : null, L.opacity < 1 ? Math.round(L.opacity * 100) + ' %' : '')),
      el('button', { class: 'lr-blend', text: info[2], title: 'Opacidad y fusión', onclick: (e) => { e.stopPropagation(); T.layers.select(i); st.push('Opacidad y fusión', () => blendView(d, L)); } }),
      el('label', { class: 'lr-vis', onclick: (e) => e.stopPropagation() },
        el('input', { type: 'checkbox', checked: L.visible, onchange: (e) => T.layers.set(L, 'visible', e.target.checked, 'Visibilidad') }), el('i')));
    row.addEventListener('click', () => {
      if (active) st.push(L.name, () => layerMenu(d, L, st));
      else T.layers.select(i);
    });
    dragToReorder(row, thumb, d, i);
    return row;
  }

  // arrastrar la miniatura para reordenar
  function dragToReorder(row, handle, d, i) {
    let drag = null;
    handle.addEventListener('pointerdown', (e) => {
      e.stopPropagation();
      handle.setPointerCapture(e.pointerId);
      drag = { y: e.clientY, h: row.offsetHeight, moved: false };
    });
    handle.addEventListener('pointermove', (e) => {
      if (!drag) return;
      const dy = e.clientY - drag.y;
      if (Math.abs(dy) > 6) drag.moved = true;
      if (drag.moved) {
        row.classList.add('dragging');
        row.style.transform = `translateY(${dy}px)`;
      }
    });
    const up = (e) => {
      if (!drag) return;
      const steps = Math.round((e.clientY - drag.y) / drag.h);
      const moved = drag.moved;
      drag = null;
      row.style.transform = '';
      row.classList.remove('dragging');
      if (moved) {
        e.preventDefault();
        row.addEventListener('click', (ev) => ev.stopImmediatePropagation(), { once: true, capture: true });
        const to = T.clamp(i - steps, 0, d.layers.length - 1); // la lista está invertida
        if (to !== i) T.layers.move(i, to);
      }
    };
    handle.addEventListener('pointerup', up);
    handle.addEventListener('pointercancel', up);
  }

  function bgRow(d) {
    const sw = el('button', { class: 'bg-swatch', style: { background: d.bg.color }, title: 'Color de fondo',
      onclick: (e) => { e.stopPropagation(); pickBgColor(d); } });
    return el('div', { class: 'layer-row bg' }, sw,
      el('div', { class: 'lr-name' }, el('span', { text: 'Color de fondo' })),
      el('label', { class: 'lr-vis' },
        el('input', { type: 'checkbox', checked: d.bg.visible, onchange: (e) => T.layers.setBg('visible', e.target.checked) }), el('i')));
  }
  async function pickBgColor(d) {
    const inp = el('input', { type: 'color', value: d.bg.color });
    const res = await U.dialog('Color de fondo', el('div', { class: 'bg-pick' }, inp,
      el('button', { class: 'pill small', text: 'Usar color actual', onclick: () => (inp.value = T.tools.color) })),
    [{ label: 'Cancelar', result: null }, { label: 'Aplicar', cls: 'primary', value: () => inp.value }]);
    if (res) T.layers.setBg('color', res);
  }

  function blendView(d, L) {
    const wrap = el('div', { class: 'menu' });
    wrap.append(U.slider('Opacidad', 0, 100, Math.round(L.opacity * 100), ' %', (v) => T.layers.set(L, 'opacity', v / 100, 'Opacidad de capa')));
    const list = el('div', { class: 'blend-list scroll' });
    T.BLENDS.forEach(([op, name]) => list.append(el('button', {
      class: 'menu-item' + (L.blend === op ? ' on' : ''), text: name,
      onclick: (e) => {
        T.layers.set(L, 'blend', op, 'Modo de fusión');
        T.$$('button', list).forEach((b) => b.classList.remove('on'));
        e.currentTarget.classList.add('on');
      },
    })));
    wrap.append(list);
    return wrap;
  }

  function layerMenu(d, L, st) {
    const i = () => d.layers.indexOf(L);
    const back = () => st.pop();
    const m = el('div', { class: 'menu scroll' },
      U.menuItem('text', 'Renombrar', async () => { const n = await U.prompt('Nombre de la capa', L.name); if (n) T.layers.rename(i(), n); back(); }),
      U.menuItem('fill', 'Rellenar con el color actual', () => { T.layers.fill(i(), T.tools.color); back(); }),
      U.menuItem('eraser', 'Borrar contenido', () => { T.layers.clear(i()); back(); }),
      U.menuItem('lock', 'Bloqueo alfa', () => { T.layers.set(L, 'alphaLock', !L.alphaLock, 'Bloqueo alfa'); back(); }, { on: L.alphaLock, right: L.alphaLock ? 'Activado' : '' }),
      U.menuItem('clip', 'Máscara de recorte', () => { T.layers.set(L, 'clip', !L.clip, 'Máscara de recorte'); back(); }, { on: L.clip, right: L.clip ? 'Activado' : '' }),
      U.menuItem('invert', 'Invertir', () => { T.layers.invert(i()); back(); }),
      U.menuItem('cursor', 'Transformar', () => T.transform.start()),
      U.menuItem('copy', 'Duplicar', () => { T.layers.duplicate(i()); back(); }),
      U.menuItem('merge', 'Combinar hacia abajo', () => { T.layers.mergeDown(i()); back(); }, { right: i() === 0 ? '—' : '' }),
      U.menuItem('up', 'Subir', () => { T.layers.move(i(), i() + 1); back(); }),
      U.menuItem('down', 'Bajar', () => { T.layers.move(i(), i() - 1); back(); }),
      U.menuItem('download', 'Exportar capa como PNG', async () => T.download(await T.toBlob(L.canvas), T.safeName(L.name) + '.png')),
      U.menuItem('trash', 'Eliminar', () => { T.layers.remove(i()); back(); }, { danger: true }));
    return m;
  }

  // ================= Color =================
  const PALETTE = [
    '#000000', '#3a3a3c', '#7c7c80', '#bdbdc2', '#ffffff', '#5b3a29', '#8d5a3b', '#c68b59', '#e8c39e', '#f6e2c9',
    '#7a0d1e', '#d7263d', '#ff6b6b', '#ff9f43', '#feca57', '#1dd1a1', '#10ac84', '#0b6e4f', '#48dbfb', '#0abde3',
    '#2e86de', '#1f3a93', '#5f27cd', '#9b59b6', '#f368e0', '#ff9ff3', '#576574', '#222f3e', '#c8d6e5', '#f4f1de',
  ];
  Pn.color = (anchor) => {
    U.popover(anchor, (box) => {
      const tabsDef = [['disc', 'Disco'], ['values', 'Valores'], ['palette', 'Paletas']];
      const swCur = el('span', { class: 'sw' }), swPrev = el('button', { class: 'sw', title: 'Color anterior', onclick: () => T.engine.setColor(T.tools.prevColor) });
      const head = el('div', { class: 'pop-head' }, el('h3', { text: 'Color' }), el('div', { class: 'grow' }), el('div', { class: 'swatch-pair' }, swCur, swPrev));
      const bar = el('div', { class: 'tabs text' });
      const body = el('div', { class: 'color-body' });
      const recent = el('div', { class: 'recent' });
      let tab = T.settings.get('colorTab', 'disc');
      let cleanup = null;
      const show = (id) => {
        tab = id;
        T.settings.set('colorTab', id);
        T.$$('button', bar).forEach((b) => b.classList.toggle('on', b.dataset.id === id));
        if (cleanup) cleanup();
        body.innerHTML = '';
        cleanup = ({ disc: discView, values: valuesView, palette: paletteView })[id](body);
      };
      tabsDef.forEach(([id, label]) => bar.append(el('button', { 'data-id': id, text: label, onclick: () => show(id) })));
      const paintHead = () => {
        swCur.style.background = T.tools.color;
        swPrev.style.background = T.tools.prevColor;
        recent.innerHTML = '';
        recent.append(el('span', { class: 'menu-label', text: 'Historial' }));
        T.tools.recent.forEach((c) => recent.append(el('button', { class: 'sw small', style: { background: c }, onclick: () => T.engine.setColor(c) })));
      };
      paintHead();
      T.on('color', () => box.isConnected && paintHead());
      box.append(head, bar, body, recent);
      show(tab);
    }, { width: 340, cls: 'color-pop' });
  };

  // Disco: anillo de tono + cuadro de saturación/brillo
  function discView(body) {
    const S = 280, dpr = window.devicePixelRatio || 1;
    const c = T.canvas(S * dpr, S * dpr);
    c.className = 'disc';
    c.style.width = c.style.height = S + 'px';
    const x = c.getContext('2d');
    const R = S / 2, rIn = R - 26, half = (rIn - 6) / Math.SQRT2;
    const draw = () => {
      const [h, s, v] = T.tools.hsv;
      x.setTransform(dpr, 0, 0, dpr, 0, 0);
      x.clearRect(0, 0, S, S);
      const g = x.createConicGradient(0, R, R);
      for (let i = 0; i <= 12; i++) g.addColorStop(i / 12, `hsl(${i * 30},100%,50%)`);
      x.fillStyle = g;
      x.beginPath();
      x.arc(R, R, R - 2, 0, Math.PI * 2);
      x.arc(R, R, rIn, 0, Math.PI * 2, true);
      x.fill();
      const x0 = R - half, y0 = R - half, w = half * 2;
      x.fillStyle = `hsl(${h},100%,50%)`;
      x.fillRect(x0, y0, w, w);
      let gr = x.createLinearGradient(x0, 0, x0 + w, 0);
      gr.addColorStop(0, '#fff'); gr.addColorStop(1, 'rgba(255,255,255,0)');
      x.fillStyle = gr; x.fillRect(x0, y0, w, w);
      gr = x.createLinearGradient(0, y0, 0, y0 + w);
      gr.addColorStop(0, 'rgba(0,0,0,0)'); gr.addColorStop(1, '#000');
      x.fillStyle = gr; x.fillRect(x0, y0, w, w);
      // marcadores
      const a = (h * Math.PI) / 180, rm = (R - 2 + rIn) / 2;
      ring(R + Math.cos(a) * rm, R + Math.sin(a) * rm, 11, `hsl(${h},100%,50%)`);
      ring(x0 + s * w, y0 + (1 - v) * w, 10, T.tools.color);
    };
    const ring = (px, py, r, fill) => {
      x.fillStyle = fill;
      x.strokeStyle = '#fff';
      x.lineWidth = 3;
      x.beginPath();
      x.arc(px, py, r, 0, Math.PI * 2);
      x.fill();
      x.stroke();
    };
    let mode = null;
    const handle = (e) => {
      const r = c.getBoundingClientRect();
      const px = e.clientX - r.left, py = e.clientY - r.top;
      const dx = px - R, dy = py - R, dd = Math.hypot(dx, dy);
      if (!mode) mode = dd > rIn - 4 ? 'hue' : 'sv';
      let [h, s, v] = T.tools.hsv;
      if (mode === 'hue') h = ((Math.atan2(dy, dx) * 180) / Math.PI + 360) % 360;
      else {
        s = T.clamp((px - (R - half)) / (half * 2), 0, 1);
        v = T.clamp(1 - (py - (R - half)) / (half * 2), 0, 1);
      }
      T.engine.setColor(T.hsvToHex(h, s, v), [h, s, v]);
    };
    c.addEventListener('pointerdown', (e) => { c.setPointerCapture(e.pointerId); mode = null; handle(e); });
    c.addEventListener('pointermove', (e) => { if (e.buttons || e.pressure) if (mode) handle(e); });
    c.addEventListener('pointerup', () => (mode = null));
    body.append(c);
    draw();
    T.on('color', () => c.isConnected && draw());
    return null;
  }

  function valuesView(body) {
    const mk = (label, max, get, set, grad) => {
      const inp = el('input', { type: 'range', min: 0, max, step: 1, value: get(), class: 'grad' });
      const num = el('input', { type: 'number', min: 0, max, value: Math.round(get()), class: 'num' });
      inp.addEventListener('input', () => { num.value = inp.value; set(Number(inp.value)); });
      num.addEventListener('change', () => { inp.value = num.value; set(T.clamp(Number(num.value), 0, max)); });
      const row = el('div', { class: 'val-row' }, el('span', { class: 'lbl', text: label }), inp, num);
      return { row, inp, num, get, grad };
    };
    const hsv = () => T.tools.hsv;
    const setHsv = (i, k) => (v) => {
      const a = hsv().slice();
      a[i] = v / k;
      T.engine.setColor(T.hsvToHex(...a), a);
    };
    const rgb = () => T.hexToRgb(T.tools.color);
    const setRgb = (i) => (v) => {
      const a = rgb();
      a[i] = v;
      T.engine.setColor(T.rgbToHex(a));
    };
    const rows = [
      mk('H', 360, () => hsv()[0], setHsv(0, 1), () => 'linear-gradient(90deg,red,#ff0,lime,cyan,blue,#f0f,red)'),
      mk('S', 100, () => hsv()[1] * 100, setHsv(1, 100), () => `linear-gradient(90deg,${T.hsvToHex(hsv()[0], 0, hsv()[2])},${T.hsvToHex(hsv()[0], 1, hsv()[2])})`),
      mk('B', 100, () => hsv()[2] * 100, setHsv(2, 100), () => `linear-gradient(90deg,#000,${T.hsvToHex(hsv()[0], hsv()[1], 1)})`),
      mk('R', 255, () => rgb()[0], setRgb(0), () => 'linear-gradient(90deg,#000,#f00)'),
      mk('G', 255, () => rgb()[1], setRgb(1), () => 'linear-gradient(90deg,#000,#0f0)'),
      mk('B', 255, () => rgb()[2], setRgb(2), () => 'linear-gradient(90deg,#000,#00f)'),
    ];
    const hex = el('input', { class: 'text-input hex', value: T.tools.color, maxlength: 7,
      onchange: () => { const c = T.hexToRgb(hex.value); if (c) T.engine.setColor(T.rgbToHex(c)); } });
    const sync = () => {
      if (!body.isConnected) return;
      rows.forEach((r) => {
        if (document.activeElement !== r.inp) r.inp.value = Math.round(r.get());
        if (document.activeElement !== r.num) r.num.value = Math.round(r.get());
        r.inp.style.background = r.grad();
      });
      if (document.activeElement !== hex) hex.value = T.tools.color;
    };
    body.append(...rows.map((r) => r.row), el('div', { class: 'val-row' }, el('span', { class: 'lbl', text: '#' }), hex));
    sync();
    T.on('color', sync);
    return null;
  }

  function paletteView(body) {
    const grid = el('div', { class: 'palette' });
    PALETTE.forEach((c) => grid.append(el('button', { class: 'sw', style: { background: c }, onclick: () => T.engine.setColor(c) })));
    const custom = el('div', { class: 'palette' });
    const renderCustom = () => {
      custom.innerHTML = '';
      const list = T.settings.get('palette', []);
      list.forEach((c, i) => {
        const b = el('button', { class: 'sw', style: { background: c }, title: 'Clic derecho o mantener para eliminar', onclick: () => T.engine.setColor(c) });
        let t;
        const del = (e) => { e && e.preventDefault(); const l = T.settings.get('palette', []); l.splice(i, 1); T.settings.set('palette', l); renderCustom(); };
        b.addEventListener('contextmenu', del);
        b.addEventListener('pointerdown', () => (t = setTimeout(del, 650)));
        b.addEventListener('pointerup', () => clearTimeout(t));
        b.addEventListener('pointerleave', () => clearTimeout(t));
        custom.append(b);
      });
      custom.append(el('button', { class: 'sw add', 'data-icon': 'plus', title: 'Añadir color actual', onclick: () => {
        const l = T.settings.get('palette', []);
        if (!l.includes(T.tools.color)) l.push(T.tools.color);
        T.settings.set('palette', l);
        renderCustom();
      } }));
      T.hydrateIcons(custom);
    };
    renderCustom();
    body.append(el('div', { class: 'menu-label', text: 'Predeterminada' }), grid, el('div', { class: 'menu-label', text: 'Mi paleta' }), custom);
    return null;
  }
})();
