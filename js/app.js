'use strict';
/* Trazo — aplicación: galería, abrir/guardar proyectos, autoguardado, PWA. */
(function () {
  const el = T.el, U = T.ui;
  const A = (T.app = { doc: null, saving: null, dirty: false });

  A.inStudio = () => !T.$('#studio').classList.contains('hidden');

  A.undo = () => {
    if (T.transform.active || T.adjust.active) return;
    const c = T.history.doUndo();
    T.toast(c ? 'Deshacer: ' + c.label : 'Nada que deshacer', 900);
    T.view.request();
  };
  A.redo = () => {
    if (T.transform.active || T.adjust.active) return;
    const c = T.history.doRedo();
    T.toast(c ? 'Rehacer: ' + c.label : 'Nada que rehacer', 900);
    T.view.request();
  };

  // ---------- Serialización ----------
  async function thumbBlob(d) {
    const k = Math.min(1, 480 / Math.max(d.width, d.height));
    const c = T.canvas(d.width * k, d.height * k);
    const x = c.getContext('2d');
    x.imageSmoothingQuality = 'high';
    x.drawImage(d.flatten(), 0, 0, c.width, c.height);
    return T.toBlob(c, 'image/jpeg', 0.85);
  }
  async function serialize(d) {
    const layers = [];
    for (const l of d.layers) {
      layers.push({ id: l.id, name: l.name, opacity: l.opacity, blend: l.blend, visible: l.visible, alphaLock: l.alphaLock, clip: l.clip, blob: await T.toBlob(l.canvas) });
    }
    const meta = { id: d.id, name: d.name, width: d.width, height: d.height, created: d.created, updated: Date.now(), layerCount: d.layers.length, thumb: await thumbBlob(d) };
    const data = { id: d.id, bg: Object.assign({}, d.bg), active: d.active, layers };
    return { meta, data };
  }
  async function deserialize(meta, data) {
    const d = new T.Doc(meta.width, meta.height, meta.name);
    d.id = meta.id;
    d.created = meta.created || Date.now();
    d.bg = Object.assign({ color: '#ffffff', visible: true }, data.bg);
    for (const s of data.layers) {
      const l = d.newLayer(s.name);
      Object.assign(l, { opacity: s.opacity, blend: s.blend, visible: s.visible, alphaLock: !!s.alphaLock, clip: !!s.clip });
      if (s.blob && s.blob.size) l.ctx.drawImage(await createImageBitmap(s.blob), 0, 0);
      d.layers.push(l);
    }
    if (!d.layers.length) d.layers.push(d.newLayer());
    d.active = T.clamp(data.active || 0, 0, d.layers.length - 1);
    return d;
  }

  A.save = async () => {
    const d = A.doc;
    if (!d) return;
    if (A.saving) {
      A.again = true;
      return A.saving;
    }
    A.dirty = false;
    A.saving = (async () => {
      try {
        const { meta, data } = await serialize(d);
        await T.store.put(meta, data);
      } catch (e) {
        console.error(e);
        T.toast('Error al guardar: ' + e.message, 3500);
      }
    })();
    await A.saving;
    A.saving = null;
    if (A.again) {
      A.again = false;
      return A.save();
    }
  };
  const autosave = T.debounce(() => { if (A.dirty) A.save(); }, 2500);
  T.on('docchange', () => {
    A.dirty = true;
    autosave();
    T.view.request();
  });

  // Exportación/importación de proyecto con capas (.trazo = JSON con PNG en base64)
  const toDataURL = (blob) => new Promise((res) => { const r = new FileReader(); r.onload = () => res(r.result); r.readAsDataURL(blob); });
  A.exportProject = async (d) => {
    const { meta, data } = await serialize(d);
    const out = { format: 'trazo', version: 1, name: meta.name, width: meta.width, height: meta.height, bg: data.bg, active: data.active, layers: [] };
    for (const l of data.layers) out.layers.push(Object.assign({}, l, { blob: undefined, png: await toDataURL(l.blob) }));
    return new Blob([JSON.stringify(out)], { type: 'application/json' });
  };
  A.importProject = async (file) => {
    const j = JSON.parse(await file.text());
    if (j.format !== 'trazo') throw new Error('No es un archivo .trazo');
    const layers = [];
    for (const l of j.layers) layers.push(Object.assign({}, l, { png: undefined, blob: await (await fetch(l.png)).blob() }));
    const d = await deserialize({ id: T.uid(), name: j.name, width: j.width, height: j.height }, { bg: j.bg, active: j.active, layers });
    return d;
  };

  // ---------- Abrir / cerrar ----------
  A.open = (d) => {
    A.doc = d;
    T.history.reset();
    T.view.overrides.clear();
    T.$('#gallery').classList.add('hidden');
    T.$('#studio').classList.remove('hidden');
    document.title = d.name + ' — Trazo';
    requestAnimationFrame(() => {
      T.view.resize();
      T.view.fit();
    });
    T.emit('docopen');
  };
  A.create = (w, h, name) => {
    const d = new T.Doc(w, h, name);
    d.layers.push(d.newLayer());
    A.open(d);
    A.dirty = true;
    A.save();
  };
  A.load = async (id) => {
    const { meta, data } = await T.store.get(id);
    if (!meta || !data) return T.toast('No se pudo abrir el proyecto');
    A.open(await deserialize(meta, data));
  };
  A.close = async () => {
    if (T.transform.active) T.transform.apply();
    if (T.adjust.active) T.adjust.cancel();
    T.input.cancelAll();
    U.closePopover();
    if (A.doc) await A.save();
    A.doc = null;
    T.history.reset();
    T.$('#studio').classList.add('hidden');
    T.$('#gallery').classList.remove('hidden');
    document.title = 'Trazo';
    renderGallery();
  };

  // ---------- Galería ----------
  const PRESETS = [
    ['Pantalla', () => [Math.round(screen.width * (devicePixelRatio || 1)), Math.round(screen.height * (devicePixelRatio || 1))]],
    ['Cuadrado', () => [2048, 2048]],
    ['4K', () => [3840, 2160]],
    ['Full HD', () => [1920, 1080]],
    ['A4 · 300 ppp', () => [2480, 3508]],
    ['Carta · 300 ppp', () => [2550, 3300]],
    ['Historia (9:16)', () => [1080, 1920]],
    ['Publicación (4:5)', () => [1080, 1350]],
  ];
  function fitSize(w, h) {
    w = Math.round(T.clamp(w, 16, T.MAX_SIDE));
    h = Math.round(T.clamp(h, 16, T.MAX_SIDE));
    if (w * h > T.MAX_AREA) {
      const k = Math.sqrt(T.MAX_AREA / (w * h));
      w = Math.floor(w * k);
      h = Math.floor(h * k);
    }
    return [w, h];
  }
  function newCanvasMenu(anchor) {
    U.popover(anchor, (box) => {
      box.append(el('div', { class: 'pop-head' }, el('h3', { text: 'Nuevo lienzo' })));
      const m = el('div', { class: 'menu scroll' });
      PRESETS.forEach(([name, f]) => {
        const [w, h] = fitSize(...f());
        m.append(U.menuItem('canvas', name, () => { U.closePopover(); A.create(w, h, 'Sin título'); }, { right: `${w} × ${h}` }));
      });
      m.append(U.menuItem('plus', 'Tamaño personalizado…', async () => {
        U.closePopover();
        const wi = el('input', { class: 'text-input', type: 'number', value: 2048, min: 16, max: T.MAX_SIDE });
        const hi = el('input', { class: 'text-input', type: 'number', value: 1536, min: 16, max: T.MAX_SIDE });
        const ni = el('input', { class: 'text-input', type: 'text', value: 'Sin título' });
        const r = await U.dialog('Lienzo personalizado', el('div', { class: 'form' },
          el('label', {}, 'Nombre', ni), el('label', {}, 'Ancho (px)', wi), el('label', {}, 'Alto (px)', hi),
          el('small', { text: `Máximo ${T.MAX_SIDE} px por lado y ${Math.round(T.MAX_AREA / 1e6)} megapíxeles.` })),
        [{ label: 'Cancelar', result: null }, { label: 'Crear', cls: 'primary', value: () => [Number(wi.value), Number(hi.value), ni.value] }]);
        if (r && r[0] && r[1]) A.create(...fitSize(r[0], r[1]), r[2] || 'Sin título');
      }));
      box.append(m);
    }, { width: 320 });
  }

  async function importIntoGallery() {
    const f = await U.pickFile('image/*,.trazo,application/json');
    if (!f) return;
    try {
      if (/\.trazo$/i.test(f.name) || f.type === 'application/json') {
        const d = await A.importProject(f);
        A.open(d);
      } else {
        const img = await createImageBitmap(f);
        const [w, h] = fitSize(img.width, img.height);
        const d = new T.Doc(w, h, f.name.replace(/\.[^.]+$/, ''));
        const l = d.newLayer('Imagen');
        l.ctx.drawImage(img, 0, 0, w, h);
        d.layers.push(l);
        A.open(d);
      }
      A.dirty = true;
      A.save();
    } catch (e) {
      T.toast('No se pudo importar: ' + e.message, 3000);
    }
  }

  async function renderGallery() {
    const grid = T.$('#g-grid');
    const items = await T.store.list();
    grid.innerHTML = '';
    T.$('#g-empty').classList.toggle('hidden', items.length > 0);
    for (const m of items) {
      const url = m.thumb ? URL.createObjectURL(m.thumb) : '';
      const img = el('img', { src: url, alt: '', draggable: false, onload: () => URL.revokeObjectURL(url) });
      const card = el('div', { class: 'card' },
        el('button', { class: 'card-img', onclick: () => A.load(m.id) }, img),
        el('div', { class: 'card-foot' },
          el('div', { class: 'card-meta' }, el('b', { text: m.name }), el('small', { text: `${m.width} × ${m.height} · ${new Date(m.updated).toLocaleDateString()}` })),
          el('button', { class: 'icon-btn', 'data-icon': 'more', title: 'Opciones', onclick: (e) => cardMenu(e.currentTarget, m) })));
      grid.append(card);
    }
    T.hydrateIcons(grid);
  }
  A.renderGallery = renderGallery;

  function cardMenu(anchor, m) {
    U.popover(anchor, (box) => {
      box.append(el('div', { class: 'menu' },
        U.menuItem('canvas', 'Abrir', () => { U.closePopover(); A.load(m.id); }),
        U.menuItem('text', 'Cambiar nombre', async () => {
          U.closePopover();
          const n = await U.prompt('Nombre', m.name);
          if (!n) return;
          m.name = n;
          await T.store.put(m);
          renderGallery();
        }),
        U.menuItem('copy', 'Duplicar', async () => {
          U.closePopover();
          const { meta, data } = await T.store.get(m.id);
          const id = T.uid();
          await T.store.put(Object.assign({}, meta, { id, name: meta.name + ' copia', updated: Date.now() }), Object.assign({}, data, { id }));
          renderGallery();
        }),
        U.menuItem('download', 'Exportar PNG', async () => {
          U.closePopover();
          const { meta, data } = await T.store.get(m.id);
          const d = await deserialize(meta, data);
          T.download(await T.toBlob(d.flatten()), T.safeName(m.name) + '.png');
        }),
        U.menuItem('share', 'Exportar .trazo', async () => {
          U.closePopover();
          const { meta, data } = await T.store.get(m.id);
          T.download(await A.exportProject(await deserialize(meta, data)), T.safeName(m.name) + '.trazo');
        }),
        U.menuItem('trash', 'Eliminar', async () => {
          U.closePopover();
          if (await U.confirm('Eliminar «' + m.name + '»', 'Esta obra se borrará de este dispositivo. No se puede deshacer.', 'Eliminar', true)) {
            await T.store.remove(m.id);
            renderGallery();
          }
        }, { danger: true })));
    }, { width: 260 });
  }

  // ---------- Arranque ----------
  function boot() {
    T.view.init(T.$('#view'));
    T.input.init(T.$('#view'));
    U.init();
    T.$('#g-new').addEventListener('click', (e) => newCanvasMenu(e.currentTarget));
    T.$('#g-new-empty').addEventListener('click', (e) => newCanvasMenu(e.currentTarget));
    T.$('#g-import').addEventListener('click', importIntoGallery);
    renderGallery();
    T.store.persistent();

    // guarda al ocultar o cerrar
    document.addEventListener('visibilitychange', () => { if (document.hidden && A.doc && A.dirty) A.save(); });
    window.addEventListener('beforeunload', (e) => {
      if (A.doc && A.dirty) { A.save(); e.preventDefault(); e.returnValue = ''; }
    });
    // evita el zoom del navegador por pellizco/Ctrl+rueda fuera del lienzo
    document.addEventListener('wheel', (e) => { if (e.ctrlKey) e.preventDefault(); }, { passive: false });
    document.addEventListener('gesturestart', (e) => e.preventDefault());

    // abrir archivos desde el sistema (PWA instalada como manejador de archivos)
    if ('launchQueue' in window) {
      window.launchQueue.setConsumer(async (p) => {
        if (!p.files || !p.files.length) return;
        const f = await p.files[0].getFile();
        try { A.open(await A.importProject(f)); A.dirty = true; A.save(); } catch (e) { T.toast('No se pudo abrir el archivo'); }
      });
    }

    if ('serviceWorker' in navigator && location.protocol.startsWith('http')) {
      navigator.serviceWorker.register('sw.js').catch(() => {});
    }
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})();
