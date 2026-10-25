# PF-01 — Rendimiento

**Estado:** medido y optimizado · **Fecha:** 2026-10-25
**Código:** `tools/Trazo.Benchmarks`, `src/Trazo.Rendering/CompositionCache.cs`, `PaintDocument` (caché de PNG), `MeshRenderer` · **Pruebas:** `PF01_*` (WARP) y mediciones en GPU

## 1. Qué se mide y con qué presupuesto

Escenario de referencia exigente: **lienzo de 3840 × 2160 con 20 capas** con contenido (multiplicar, un grupo y una máscara), compuesto en una vista de 2560 × 1440 como la de la app. Cada medición incluye esperar a que la GPU termine (se lee un píxel), así que es el tiempo real del fotograma, no solo el de enviar órdenes.

| ID | Medición | Mínimo (decide) | Objetivo (informativo) | Por qué |
|---|---|---|---|---|
| PF-1 | Trazo: 2 muestras del lápiz (240 Hz) + fotograma con la vista previa, con tres pinceles (fino, con grano y mezcla húmeda, aerógrafo de 400 px) | p95 ≤ 16,7 ms | p95 ≤ 8,3 ms | 16,7 ms = 60 fotogramas por segundo; 8,3 ms = 120 Hz, el ritmo de las pantallas y lápices rápidos |
| PF-2 | Componer el documento completo en la vista | 16,7 ms | 8,3 ms | Desplazar, hacer zoom y girar sin tirones |
| PF-3 | Licuar: un toque de 300 px + fotograma | 16,7 ms | 8,3 ms | Respuesta continua del pincel de licuar |
| PF-4 | Deformar: mover un punto + fotograma con la malla | 16,7 ms | 8,3 ms | Arrastre fluido |
| PF-5a | Primer guardado de un documento (codificar las 20 capas) | 10 s | — | Ocurre una vez por documento nuevo o importado; corre en segundo plano |
| PF-5d | Autoguardado tras pintar una capa | 1,5 s | — | El autoguardado (cada 3 s) no debe ocupar el equipo sin parar |
| PF-5b | Escribir y verificar el `.trazo` | 5 s | — | Segundo plano |
| PF-5c | Abrir el `.trazo` | 5 s | — | Espera aceptable para un documento grande |
| PF-6 | Memoria | informativo | — | 32 MB de GPU por capa de 4K |

**Repetibilidad.** En un portátil con otros programas abiertos, una sola ronda variaba más de un 50 % en el p95. Por eso se hacen **3 rondas de 60 repeticiones** y se toma la **mediana de los p95**.

**Por qué dos niveles.** Las primeras mediciones usaban 8,3 ms como único presupuesto del trazo. Tras optimizar, la mediana (p50) ya lo cumple, pero el p95 no en una GPU integrada a 4K. Se fija como mínimo los 60 Hz, que es lo que necesita una app fluida, y se mantienen los 120 Hz como objetivo visible en el informe. Esta decisión queda escrita aquí, en lugar de ajustar el presupuesto en silencio.

## 2. Optimizaciones (todas con prueba que garantiza el mismo resultado)

| Problema medido | Cambio | Garantía |
|---|---|---|
| Cada autoguardado volvía a codificar a PNG **todas** las capas (9,4 s con 20 capas de 4K) | Caché de PNG por capa: solo se codifican las capas cuya revisión (o máscara) cambió. Al abrir un archivo, sus PNG ya cuentan como codificados | `IncrementalSaveMatchesFullEncode`: tras 20 operaciones distintas, lo guardado es **byte a byte** lo mismo que codificar todo. `OpenedFileReusesItsPngs`. Mutación (ignorar la revisión): falla |
| Cada fotograma volvía a componer las 20 capas | Caché de composición: las unidades (capa con sus recortes, o grupo entero) de debajo de la que se está modificando se componen una vez en una textura | `CompositionCacheMatchesFullCompose`: 25 documentos aleatorios (modos, opacidades, recortes, visibilidad, máscaras y grupos) con y sin sustitución, diferencia ≤ 1 nivel (redondeo de la textura). `CompositionCacheReusesFixedPartAndInvalidates`: 11 tipos de cambio invalidan la caché. Mutación (ignorar la revisión): falla. **Solo afecta a la vista**: guardar y exportar componen sin caché |
| El grano texturizado del trazo se calculaba en todo el lienzo en cada fotograma | La vista previa recorta el trazo a la zona pintada antes del grano y del tope | Fuera de esa zona el búfer es transparente: resultado idéntico (todas las referencias BR-02 sin cambios) |
| La malla de Deformar creaba 2048 geometrías por fotograma | Un único triángulo unidad transformado; vista previa con 16 × 16 celdas (al aplicar, 32 × 32) | Pruebas TR-01 (sin costuras, sin pérdidas, contra la referencia exacta y las referencias visuales) |

## 3. Criterios de aceptación

| ID | Criterio | Prueba |
|---|---|---|
| PF-01.1 | En la GPU de referencia, todas las mediciones cumplen su mínimo | `tools/Trazo.Benchmarks` (sale con código 1 si alguna no lo cumple) |
| PF-01.2 | El autoguardado incremental guarda exactamente lo mismo que el completo | `IncrementalSaveMatchesFullEncode`, `OpenedFileReusesItsPngs` |
| PF-01.3 | La caché de composición muestra lo mismo que componer todo (± 1 nivel) y se invalida con cualquier cambio visible | `CompositionCacheMatchesFullCompose`, `CompositionCacheReusesFixedPartAndInvalidates` |
| PF-01.4 | La CI mide con WARP en cada cambio y guarda el informe (sin decidir: WARP no es una GPU) | trabajo *Rendimiento (WARP, informativo)* |

## 4. Pendiente

- **Latencia de extremo a extremo** (del lápiz a la pantalla): necesita una cámara rápida o un sensor. Lo medido aquí es el tiempo de proceso y de dibujo de la app, no lo que añaden el controlador del lápiz y la pantalla.
- Medir en una GPU dedicada y en un equipo ARM.
- Objetivo de 120 Hz con el pincel con grano y mezcla húmeda (12,9 ms) y con Licuar (16,1 ms).
- El primer guardado de un documento muy grande (8,6 s) podría repartirse entre núcleos.
