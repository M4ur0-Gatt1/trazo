'use strict';
/* Trazo — almacenamiento de proyectos en IndexedDB (con respaldo en memoria). */
(function () {
  const DB = 'trazo';
  const VERSION = 1;
  const mem = { meta: new Map(), data: new Map() };
  let dbp = null;

  function open() {
    if (dbp) return dbp;
    dbp = new Promise((res) => {
      try {
        const r = indexedDB.open(DB, VERSION);
        r.onupgradeneeded = () => {
          const db = r.result;
          if (!db.objectStoreNames.contains('meta')) db.createObjectStore('meta', { keyPath: 'id' });
          if (!db.objectStoreNames.contains('data')) db.createObjectStore('data', { keyPath: 'id' });
        };
        r.onsuccess = () => res(r.result);
        r.onerror = () => res(null);
        r.onblocked = () => res(null);
      } catch (e) {
        res(null);
      }
    });
    return dbp;
  }

  function run(db, stores, mode, fn) {
    return new Promise((res, rej) => {
      const t = db.transaction(stores, mode);
      const r = fn(t);
      t.oncomplete = () => res(r && 'result' in r ? r.result : undefined);
      t.onerror = () => rej(t.error);
      t.onabort = () => rej(t.error || new Error('Transacción abortada'));
    });
  }

  T.store = {
    async list() {
      const db = await open();
      const all = db ? await run(db, 'meta', 'readonly', (t) => t.objectStore('meta').getAll()) : [...mem.meta.values()];
      return (all || []).sort((a, b) => b.updated - a.updated);
    },
    async get(id) {
      const db = await open();
      if (!db) return { meta: mem.meta.get(id), data: mem.data.get(id) };
      let meta, data;
      await run(db, ['meta', 'data'], 'readonly', (t) => {
        const a = t.objectStore('meta').get(id);
        const b = t.objectStore('data').get(id);
        a.onsuccess = () => (meta = a.result);
        b.onsuccess = () => (data = b.result);
      });
      return { meta, data };
    },
    async put(meta, data) {
      const db = await open();
      if (!db) {
        mem.meta.set(meta.id, meta);
        if (data) mem.data.set(meta.id, data);
        return;
      }
      await run(db, ['meta', 'data'], 'readwrite', (t) => {
        t.objectStore('meta').put(meta);
        if (data) t.objectStore('data').put(data);
      });
    },
    async remove(id) {
      const db = await open();
      if (!db) {
        mem.meta.delete(id);
        mem.data.delete(id);
        return;
      }
      await run(db, ['meta', 'data'], 'readwrite', (t) => {
        t.objectStore('meta').delete(id);
        t.objectStore('data').delete(id);
      });
    },
    async persistent() {
      try {
        if (navigator.storage && navigator.storage.persist) await navigator.storage.persist();
      } catch (e) { /* opcional */ }
    },
  };
})();
