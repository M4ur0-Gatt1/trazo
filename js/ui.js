'use strict';
/* Trazo — componentes de interfaz: popovers, hojas inferiores, diálogos,
   barra superior, barra lateral y menús de Acciones / Ajustes. */
(function () {
  const U = (T.ui = {});
  const el = T.el;

  // ---------- Popover ----------
  let pop = null;
  let lastClosed = { anchor: null, t: 0 };
  U.popover = (anchor, build, opts = {}) => {
    // tocar el botón de un panel abierto lo cierra (el fondo ya lo cerró en pointerdown)
    const reopen = (pop && pop.anchor === anchor) || (lastClosed.anchor === anchor && performance.now() - lastClosed.t < 500);
    U.closePopover();
    lastClosed.anchor = null;
    if (reopen && !opts.force) return null;
    const layer = T.$('#popover-layer');
    const back = el('div', { class: 'pop-backdrop', onpointerdown: (e) => { e.preventDefault(); U.closePopover(); } });
    const box = el('div', { class: 'popover ' + (opts.cls || '') });
    const width = Math.min(opts.width || 340, innerWidth - 16);
    box.style.width = width + 'px';
    layer.append(back, box);
    pop = { el: box, back, anchor, onClose: opts.onClose };
    build(box, pop);
    T.hydrateIcons(box);
    const r = anchor.getBoundingClientRect();
    let left = opts.align === 'left' ? r.left : r.right - width;
    left = T.clamp(left, 8, innerWidth - width - 8);
    const top = r.bottom + 10;
    box.style.left = left + 'px';
    box.style.top = top + 'px';
    box.style.maxHeight = innerHeight - top - 12 + 'px';
    anchor.classList.add('open');
    return pop;
  };
  U.closePopover = () => {
    if (!pop) return;
    const p = pop;
    pop = null;
    lastClosed = { anchor: p.anchor, t: performance.now() };
    p.el.remove();
    p.back.remove();
    p.anchor.classList.remove('open');
    if (p.onClose) p.onClose();
  };
  U.popoverOpen = () => !!pop;

  // Vista apilable dentro de un popover (con botón atrás)
  U.stack = (box) => {
    const views = [];
    const show = () => {
      box.innerHTML = '';
      const v = views[views.length - 1];
      const head = el('div', { class: 'pop-head' },
        views.length > 1 ? el('button', { class: 'icon-btn', 'data-icon': 'back', onclick: () => { views.pop(); show(); } }) : null,
        el('h3', { text: v.title }),
        el('div', { class: 'grow' }),
        v.actions || null);
      box.append(head, v.body());
      T.hydrateIcons(box);
    };
    return {
      push(title, body, actions) { views.push({ title, body, actions }); show(); },
      pop() { views.pop(); show(); },
      refresh: show,
      depth: () => views.length,
    };
  };

  // ---------- Hoja inferior (ajustes, transformar) ----------
  let sheetOpts = null;
  U.sheet = (o) => {
    sheetOpts = o;
    const s = T.$('#sheet');
    s.innerHTML = '';
    s.append(
      el('div', { class: 'sheet-head' },
        el('button', { class: 'pill', text: 'Cancelar', onclick: () => o.onCancel() }),
        el('h3', { text: o.title }),
        el('button', { class: 'pill primary', text: 'Aplicar', onclick: () => o.onApply() })),
      el('div', { class: 'sheet-body' }, o.body));
    T.hydrateIcons(s);
    s.classList.remove('hidden');
    document.body.classList.add('sheet-open');
  };
  U.closeSheet = () => {
    sheetOpts = null;
    T.$('#sheet').classList.add('hidden');
    document.body.classList.remove('sheet-open');
  };

  // ---------- Controles ----------
  U.slider = (label, min, max, value, unit, onInput, step = 1) => {
    const out = el('span', { class: 'val', text: Math.round(value) + unit });
    const inp = el('input', {
      type: 'range', min, max, step, value,
      oninput: () => { out.textContent = (step < 1 ? Number(inp.value).toFixed(2) : Math.round(inp.value)) + unit; onInput(Number(inp.value)); },
    });
    if (step < 1) out.textContent = Number(value).toFixed(2) + unit;
    return el('label', { class: 'row-slider' }, el('span', { class: 'lbl', text: label }), inp, out);
  };
  U.toggle = (label, value, onChange, hint) => {
    const inp = el('input', { type: 'checkbox', checked: !!value, onchange: () => onChange(inp.checked) });
    return el('label', { class: 'row-toggle' },
      el('span', { class: 'lbl' }, label, hint ? el('small', { text: hint }) : null),
      el('span', { class: 'switch' }, inp, el('i')));
  };
  U.chipRow = (items) =>
    el('div', { class: 'chips' }, items.map(([icon, label, fn]) =>
      el('button', { class: 'chip', onclick: fn }, el('span', { 'data-icon': icon }), label)));
  U.segmented = (options, value, onChange) => {
    const wrap = el('div', { class: 'segmented' });
    options.forEach(([v, label]) => {
      const b = el('button', { class: v === value ? 'on' : '', text: label, onclick: () => {
        T.$$('button', wrap).forEach((x) => x.classList.remove('on'));
        b.classList.add('on');
        onChange(v);
      } });
      wrap.append(b);
    });
    return wrap;
  };
  U.menuItem = (icon, label, fn, opts = {}) =>
    el('button', { class: 'menu-item' + (opts.danger ? ' danger' : '') + (opts.on ? ' on' : ''), onclick: fn },
      icon ? el('span', { class: 'mi-icon', 'data-icon': icon }) : null,
      el('span', { class: 'mi-label', text: label }),
      opts.right ? el('span', { class: 'mi-right', text: opts.right }) : null);

  // ---------- Diálogos ----------
  U.dialog = (title, body, buttons) =>
    new Promise((res) => {
      const layer = T.$('#modal-layer');
      const box = el('div', { class: 'dialog' }, el('h3', { text: title }), body || null);
      const row = el('div', { class: 'dialog-buttons' });
      const close = (v) => { wrap.remove(); res(v); };
      buttons.forEach((b) => row.append(el('button', { class: 'pill ' + (b.cls || ''), text: b.label, onclick: () => close(b.value ? b.value() : b.result) })));
      box.append(row);
      const wrap = el('div', { class: 'modal' }, box);
      wrap.addEventListener('pointerdown', (e) => { if (e.target === wrap) close(undefined); });
      layer.append(wrap);
      const f = box.querySelector('input');
      if (f) setTimeout(() => { f.focus(); f.select && f.select(); }, 30);
      box.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { const ok = buttons[buttons.length - 1]; close(ok.value ? ok.value() : ok.result); }
        if (e.key === 'Escape') close(undefined);
      });
    });
  U.prompt = (title, value = '') => {
    const inp = el('input', { class: 'text-input', type: 'text', value });
    return U.dialog(title, inp, [{ label: 'Cancelar', result: null }, { label: 'Aceptar', cls: 'primary', value: () => inp.value.trim() }]);
  };
  U.confirm = (title, msg, ok = 'Aceptar', danger) =>
    U.dialog(title, el('p', { text: msg }), [{ label: 'Cancelar', result: false }, { label: ok, cls: danger ? 'danger' : 'primary', result: true }]);

  U.toggleFullscreen = () => {
    document.body.classList.toggle('ui-hidden');
    U.closePopover();
  };

  // ---------- Barra lateral: sliders verticales ----------
  function vslider(root, get, set, fmt) {
    root.innerHTML = '<div class="vs-track"><div class="vs-fill"></div></div><div class="vs-thumb"></div><div class="vs-bubble"></div>';
    const fill = root.querySelector('.vs-fill'), thumb = root.querySelector('.vs-thumb'), bubble = root.querySelector('.vs-bubble');
    const update = () => {
      const t = get();
      fill.style.height = t * 100 + '%';
      thumb.style.top = `calc(8px + (100% - 16px) * ${1 - t})`;
    };
    let drag = null;
    root.addEventListener('pointerdown', (e) => {
      e.preventDefault();
      root.setPointerCapture(e.pointerId);
      drag = { y: e.clientY, t: get(), h: root.clientHeight };
      root.classList.add('drag');
      bubble.innerHTML = fmt();
    });
    root.addEventListener('pointermove', (e) => {
      if (!drag) return;
      // relativo: más preciso con el dedo
      set(T.clamp(drag.t - (e.clientY - drag.y) / drag.h, 0, 1));
      update();
      bubble.innerHTML = fmt();
    });
    const up = () => { drag = null; root.classList.remove('drag'); };
    root.addEventListener('pointerup', up);
    root.addEventListener('pointercancel', up);
    return update;
  }

  const sizeT = () => {
    const b = T.engine.brush(), s = T.engine.state().size;
    return Math.sqrt(T.clamp((s - b.minSize) / (b.maxSize - b.minSize), 0, 1));
  };
  const setSizeT = (t) => {
    const b = T.engine.brush();
    T.engine.setSize(b.minSize + (b.maxSize - b.minSize) * t * t);
  };

  // ---------- Inicialización de la barra del estudio ----------
  U.init = () => {
    T.hydrateIcons();
    const updSize = vslider(T.$('#slider-size'), sizeT, setSizeT, () => {
      const s = T.engine.state().size;
      const px = T.clamp(s * T.view.scale, 2, 120);
      return `<i style="width:${px}px;height:${px}px"></i><b>${s < 10 ? s.toFixed(1) : Math.round(s)} px</b>`;
    });
    const updOp = vslider(T.$('#slider-opacity'), () => T.engine.state().opacity, (t) => T.engine.setOpacity(t), () => `<b>${Math.round(T.engine.state().opacity * 100)} %</b>`);
    const refreshSliders = () => { updSize(); updOp(); };
    T.on('tool', refreshSliders);
    T.on('toolvalues', refreshSliders);
    refreshSliders();

    // herramientas
    T.$$('[data-tool]').forEach((b) =>
      b.addEventListener('click', () => {
        const t = b.dataset.tool;
        if (T.tools.current === t) T.panels.brushLibrary(b, t);
        else { U.closePopover(); T.engine.setTool(t); }
      }));
    const markTool = () => T.$$('[data-tool]').forEach((b) => b.classList.toggle('active', b.dataset.tool === T.tools.current));
    T.on('tool', markTool);
    markTool();

    T.$('#btn-gallery').addEventListener('click', () => T.app.close());
    T.$('[data-action=actions]').addEventListener('click', (e) => U.actions(e.currentTarget));
    T.$('[data-action=adjust]').addEventListener('click', (e) => U.adjustMenu(e.currentTarget));
    T.$('[data-action=transform]').addEventListener('click', () => (T.transform.active ? T.transform.apply() : T.transform.start()));
    T.$('[data-action=layers]').addEventListener('click', () => U.openLayers());
    T.on('transform', (on) => T.$('[data-action=transform]').classList.toggle('active', on));

    T.$('#btn-undo').addEventListener('click', () => T.app.undo());
    T.$('#btn-redo').addEventListener('click', () => T.app.redo());
    const updHist = () => {
      T.$('#btn-undo').disabled = !T.history.undo.length;
      T.$('#btn-redo').disabled = !T.history.redo.length;
    };
    T.on('history', updHist);
    updHist();

    const eye = T.$('#btn-eyedrop');
    eye.addEventListener('click', () => {
      T.input.eyedropOnce = !T.input.eyedropOnce;
      T.emit('eyedropmode', T.input.eyedropOnce);
    });
    T.on('eyedropmode', (on) => eye.classList.toggle('active', on));

    // botón de color: toque = panel; arrastrar al lienzo = relleno (ColorDrop)
    const cb = T.$('#btn-color');
    const paint = () => { cb.style.background = T.tools.color; };
    T.on('color', paint);
    paint();
    let drag = null;
    const dot = el('div', { class: 'drop-dot hidden' });
    document.body.append(dot);
    cb.addEventListener('pointerdown', (e) => {
      cb.setPointerCapture(e.pointerId);
      drag = { x: e.clientX, y: e.clientY, active: false };
    });
    cb.addEventListener('pointermove', (e) => {
      if (!drag) return;
      if (!drag.active && Math.hypot(e.clientX - drag.x, e.clientY - drag.y) > 14) {
        drag.active = true;
        U.closePopover();
        dot.style.background = T.tools.color;
        dot.classList.remove('hidden');
      }
      if (drag.active) dot.style.transform = `translate(${e.clientX - 22}px, ${e.clientY - 22}px)`;
    });
    cb.addEventListener('pointerup', (e) => {
      const d = drag;
      drag = null;
      dot.classList.add('hidden');
      if (!d) return;
      if (!d.active) return U.openColor();
      const hit = document.elementFromPoint(e.clientX, e.clientY);
      if (hit && hit.id === 'view') T.input.fillAtClient(e.clientX, e.clientY);
    });
    cb.addEventListener('pointercancel', () => { drag = null; dot.classList.add('hidden'); });

    const placeSidebar = () => T.$('#sidebar').classList.toggle('right', T.settings.get('leftHanded', false));
    placeSidebar();
    T.on('setting', (k) => { if (k === 'leftHanded') placeSidebar(); });
  };

  U.openLayers = () => T.panels.layers(T.$('[data-action=layers]'));
  U.openColor = () => T.panels.color(T.$('#btn-color'));

  // ---------- Menú Ajustes ----------
  U.adjustMenu = (anchor) => {
    U.popover(anchor, (box) => {
      box.append(el('div', { class: 'pop-head' }, el('h3', { text: 'Ajustes' })));
      const list = el('div', { class: 'menu scroll' });
      T.adjustDefs.forEach((d) => list.append(U.menuItem(null, d.name, () => T.adjust.open(d.id))));
      box.append(list);
    }, { width: 300, align: 'left' });
  };

  // ---------- Menú Acciones ----------
  U.actions = (anchor) => {
    U.popover(anchor, (box) => {
      const tabs = [
        ['add', 'Añadir', 'plus'], ['canvas', 'Lienzo', 'canvas'], ['share', 'Compartir', 'share'],
        ['prefs', 'Preferencias', 'prefs'], ['help', 'Ayuda', 'help'],
      ];
      const content = el('div', { class: 'menu scroll' });
      const bar = el('div', { class: 'tabs' });
      const show = (id) => {
        T.$$('button', bar).forEach((b) => b.classList.toggle('on', b.dataset.id === id));
        content.innerHTML = '';
        content.append(...actionTabs[id]());
        T.hydrateIcons(content);
        T.settings.set('actionsTab', id);
      };
      tabs.forEach(([id, label, icon]) => bar.append(el('button', { 'data-id': id, onclick: () => show(id) }, el('span', { 'data-icon': icon }), el('small', { text: label }))));
      box.append(el('div', { class: 'pop-head' }, el('h3', { text: 'Acciones' })), bar, content);
      show(T.settings.get('actionsTab', 'add'));
    }, { width: 380, align: 'left' });
  };

  const pickFile = (accept) =>
    new Promise((res) => {
      const inp = el('input', { type: 'file', accept, style: { display: 'none' } });
      inp.addEventListener('change', () => { res(inp.files[0] || null); inp.remove(); });
      document.body.append(inp);
      inp.click();
    });
  U.pickFile = pickFile;
  U.loadImage = (blob) => createImageBitmap(blob);

  const actionTabs = {
    add: () => [
      U.menuItem('image', 'Insertar una foto', async () => {
        U.closePopover();
        const f = await pickFile('image/*');
        if (!f) return;
        try {
          const img = await U.loadImage(f);
          if (T.layers.addImage(img, f.name.replace(/\.[^.]+$/, ''))) T.transform.start();
        } catch (e) { T.toast('No se pudo abrir la imagen'); }
      }),
      U.menuItem('copy', 'Pegar imagen del portapapeles', async () => {
        U.closePopover();
        try {
          const items = await navigator.clipboard.read();
          for (const it of items) {
            const type = it.types.find((t) => t.startsWith('image/'));
            if (type) {
              const img = await U.loadImage(await it.getType(type));
              if (T.layers.addImage(img, 'Pegado')) T.transform.start();
              return;
            }
          }
          T.toast('No hay ninguna imagen en el portapapeles');
        } catch (e) { T.toast('El navegador no permitió leer el portapapeles'); }
      }),
      U.menuItem('text', 'Añadir texto', async () => {
        U.closePopover();
        const txt = await U.prompt('Texto', '');
        if (!txt) return;
        const d = T.app.doc;
        const l = d.newLayer(txt.slice(0, 24));
        const size = Math.round(Math.min(d.width, d.height) / 10);
        l.ctx.fillStyle = T.tools.color;
        l.ctx.font = `600 ${size}px "Segoe UI", system-ui, sans-serif`;
        l.ctx.textAlign = 'center';
        l.ctx.textBaseline = 'middle';
        txt.split('\n').forEach((line, i, arr) => l.ctx.fillText(line, d.width / 2, d.height / 2 + (i - (arr.length - 1) / 2) * size * 1.2));
        if (T.layers.add(l)) T.transform.start();
      }),
      U.menuItem('layers', 'Nueva capa', () => { T.layers.add(); U.closePopover(); }),
    ],
    canvas: () => {
      const d = T.app.doc;
      return [
        U.menuItem('flipH', 'Voltear lienzo horizontalmente', () => T.layers.flipCanvas(true)),
        U.menuItem('flipV', 'Voltear lienzo verticalmente', () => T.layers.flipCanvas(false)),
        U.menuItem('fit', 'Ajustar a la pantalla', () => { T.view.fit(); U.closePopover(); }),
        U.menuItem('eye', 'Tamaño real (100 %)', () => { T.view.actualSize(); U.closePopover(); }),
        el('div', { class: 'menu-label', text: 'Guía de simetría' }),
        U.segmented([['none', 'No'], ['vertical', 'Vertical'], ['horizontal', 'Horiz.'], ['quadrant', 'Cuadrante'], ['radial', 'Radial']], T.tools.symmetry, (v) => T.engine.setSymmetry(v)),
        U.menuItem('text', 'Cambiar nombre del lienzo', async () => {
          const n = await U.prompt('Nombre del lienzo', d.name);
          if (n) { d.name = n; T.emit('docchange'); }
        }),
        el('div', { class: 'info' },
          el('div', {}, el('span', { text: 'Dimensiones' }), el('b', { text: `${d.width} × ${d.height} px` })),
          el('div', {}, el('span', { text: 'Capas' }), el('b', { text: `${d.layers.length} / ${d.maxLayers()}` })),
          el('div', {}, el('span', { text: 'Pasos de historial' }), el('b', { text: String(T.history.undo.length) }))),
      ];
    },
    share: () => {
      const d = T.app.doc;
      const name = T.safeName(d.name);
      return [
        U.menuItem('download', 'Exportar PNG', async () => T.download(await T.toBlob(d.flatten()), name + '.png')),
        U.menuItem('download', 'Exportar PNG transparente (sin fondo)', async () => T.download(await T.toBlob(d.flatten({ noBg: true })), name + '.png')),
        U.menuItem('download', 'Exportar JPEG', async () => {
          const c = d.flatten();
          if (!d.bg.visible) {
            const k = T.canvas(c.width, c.height), x = k.getContext('2d');
            x.fillStyle = '#fff'; x.fillRect(0, 0, c.width, c.height); x.drawImage(c, 0, 0);
            return T.download(await T.toBlob(k, 'image/jpeg', 0.92), name + '.jpg');
          }
          T.download(await T.toBlob(c, 'image/jpeg', 0.92), name + '.jpg');
        }),
        U.menuItem('layers', 'Exportar capas como PNG', async () => {
          for (let i = 0; i < d.layers.length; i++) {
            T.download(await T.toBlob(d.layers[i].canvas), `${name}-${String(i + 1).padStart(2, '0')}-${T.safeName(d.layers[i].name)}.png`);
            await new Promise((r) => setTimeout(r, 250));
          }
        }),
        U.menuItem('share', 'Exportar proyecto .trazo (con capas)', async () => T.download(await T.app.exportProject(d), name + '.trazo')),
        navigator.share ? U.menuItem('share', 'Compartir…', async () => {
          try {
            const f = new File([await T.toBlob(d.flatten())], name + '.png', { type: 'image/png' });
            await navigator.share({ files: [f], title: d.name });
          } catch (e) { /* cancelado */ }
        }) : null,
      ].filter(Boolean);
    },
    prefs: () => [
      U.toggle('Solo lápiz', T.input.penOnly, (v) => T.input.setPenOnly(v), 'El dedo navega y el lápiz dibuja'),
      U.toggle('Interfaz para zurdos', T.settings.get('leftHanded', false), (v) => T.settings.set('leftHanded', v), 'Barra lateral a la derecha'),
      U.toggle('Rotar con dos dedos', T.settings.get('rotateGesture', true), (v) => T.settings.set('rotateGesture', v)),
      U.toggle('QuickShape', T.settings.get('quickShape', true), (v) => T.settings.set('quickShape', v), 'Mantén al final del trazo para crear formas'),
      U.toggle('Cursor del pincel', T.settings.get('brushCursor', true), (v) => { T.settings.set('brushCursor', v); T.view.request(); }),
      U.slider('Curva de presión', 0.3, 2.5, T.settings.get('pressureGamma', 1), '', (v) => T.settings.set('pressureGamma', v), 0.05),
      U.slider('Umbral de relleno', 0, 100, T.settings.get('fillThreshold', 18), ' %', (v) => T.settings.set('fillThreshold', v)),
    ],
    help: () => [
      el('div', { class: 'help' }, el('dl', {}, ...[
        ['Tocar con dos dedos', 'Deshacer'],
        ['Tocar con tres dedos', 'Rehacer'],
        ['Tocar con cuatro dedos', 'Ocultar / mostrar la interfaz'],
        ['Pellizcar / girar con dos dedos', 'Zoom, desplazar y rotar el lienzo'],
        ['Pellizco rápido', 'Ajustar el lienzo a la pantalla'],
        ['Tocar y mantener (dedo)', 'Cuentagotas'],
        ['Mantener al final del trazo', 'QuickShape: línea, círculo, elipse'],
        ['Arrastrar el círculo de color', 'Relleno de color'],
        ['Botón lateral del lápiz', 'Cuentagotas'],
        ['Extremo borrador del lápiz', 'Borrador temporal'],
        ['B · E · S', 'Pincel · Borrador · Difuminar'],
        ['[ y ]', 'Tamaño del pincel'],
        ['1 … 9', 'Opacidad 10 … 90 %'],
        ['I · G · V · X', 'Cuentagotas · Relleno · Transformar · Color anterior'],
        ['Ctrl+Z · Ctrl+Y', 'Deshacer · Rehacer'],
        ['Espacio + arrastrar', 'Desplazar'],
        ['Rueda · Ctrl+rueda · Alt+rueda', 'Zoom · Zoom · Rotar'],
        ['Ctrl+0 · Ctrl+1 · R', 'Ajustar · 100 % · Quitar rotación'],
        ['Tab', 'Pantalla completa'],
      ].map(([a, b]) => [el('dt', { text: a }), el('dd', { text: b })]).flat())),
      el('p', { class: 'hint', text: 'Trazo es software libre (MIT). Tus obras se guardan solo en este dispositivo.' }),
    ],
  };
})();
