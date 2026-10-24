# ADR-0005 — Persistencia (decisión E)

**Estado:** aceptada (formato v2 + guardado transaccional) · **Fecha:** 2026-10-24

## Decisión

1. **El documento en memoria y el formato están separados.** `PaintDocument` (GPU) ↔ `ProjectData` (datos puros) ↔ `TrazoFile` (bytes). Solo `ProjectData` cruza la frontera.
2. **Formato `.trazo` v2:** un ZIP con `manifest.json` (versión, dimensiones, fondo, capa activa y propiedades de cada capa), `layers/NNN.png` (una PNG sin premultiplicar por capa) y `thumbnail.png`. Es abierto, se inspecciona con cualquier visor de ZIP y es tolerante a datos desconocidos.
3. **Versionado:** `version` en el manifiesto. El lector rechaza versiones futuras con un mensaje claro y **migra** el formato web v1 (JSON con PNG en base64).
4. **Validación:** dimensiones dentro de los límites; cada capa es un PNG cuya cabecera IHDR coincide con el tamaño del lienzo.
5. **Transacción de guardado y recuperación:** ver [FI-01](../protocols/FI-01.md). Temporal, volcado a disco, verificación releyendo, reemplazo atómico con `.bak`, y recuperación desde `.bak` o `.tmp` al abrir.

## Alternativas

| Opción | Motivo para descartarla por ahora |
|---|---|
| SQLite con mosaicos | Mejor para guardados incrementales de documentos enormes; se reconsiderará junto con ADR-0001 (mosaicos) |
| Formato binario propio | Menos transparente y más difícil de depurar y migrar |
| Compatibilidad con `.procreate` | Fuera de alcance; requiere investigación técnica y legal aparte (EX-01) |

## Pendiente

- Guardado incremental: hoy se reescriben todas las capas en cada autoguardado. Medir el coste con documentos grandes (PF-01).
- Una prueba de cierre forzado del proceso durante el guardado.
- Migraciones: cuando exista la v3, añadir pruebas con archivos v1 y v2 reales como fijaciones.
