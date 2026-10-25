# Matriz de requisitos

Estado al **2026-10-25** (commit de esta revisión). Las prioridades son propuestas de ingeniería, no clasificaciones de Procreate.

Leyenda de las puertas: ✅ superada con evidencia · 🟡 parcial · ⬜ pendiente.
Columnas: **Inv**estigación · **Esp**ecificación · **Impl**ementación · **Pru**ebas · **Apr**obación.

## Resumen por área

| ID | Área | Prioridad | Inv | Esp | Impl | Pru | Apr | Evidencia o bloqueo |
|---|---|---|---|---|---|---|---|---|
| BR-01 | Entrada del lápiz y trayectoria | Crítica | 🟡 | ✅ | ✅ | ✅ | 🟡 | [Protocolo](protocols/BR-01.md); 16 pruebas con entrada sintética. Falta validar con lápiz real y medir la latencia (PF-01) |
| BR-02 | Reproducibilidad del motor de pinceles | Crítica | ✅ | ✅ | ✅ | ✅ | ✅ | [Protocolo](protocols/BR-02.md); 21 referencias revisadas; prueba de mutación superada; métrica endurecida el 2026-10-25 (% sobre píxeles con tinta) |
| BR-03 | Texturas y grano | Alta | 🟡 | ✅ | 🟡 | ✅ | 🟡 | [Especificación](specs/BR-03-grain.md): Moving/Texturized, Movement, Scale, Zoom, Rotation, Depth, Offset Jitter, Brillo y Contraste; 13 pruebas y 5 referencias. Faltan Depth Min/Jitter, el modo de fusión del grano, el filtrado y la importación. Sin observación en iPad |
| BR-04 | Mezcla húmeda | Crítica | 🟡 | ✅ | ✅ | ✅ | 🟡 | [Especificación](specs/BR-04-wet-mix.md): dilución, carga, ataque y arrastre del color de la capa (al empezar el trazo), deterministas; 7 pruebas de núcleo y 2 WARP; 3 referencias BR-02 cambiadas a propósito. Falta observar la respuesta de Procreate; sin grado, desenfoque ni mezcla con otras capas |
| BR-05 | Afinado (taper) | Alta | 🟡 | ✅ | 🟡 | ✅ | 🟡 | [Especificación](specs/BR-05-taper.md): Pressure y Touch Taper (inicio, final, punta, opacidad y forma) con cola provisional; 13 pruebas y 2 referencias. Faltan Pressure (reactivo), Link Tip Sizes y Classic. Sin observación en iPad |
| BR-06 | Curvas de dinámica por parámetro | Alta | 🟡 | ✅ | ✅ | ✅ | 🟡 | [Especificación](specs/BR-06-dynamics.md): curvas monótonas de presión para tamaño, flujo y opacidad; opacidad por presión como tope separado del flujo. 10 pruebas de núcleo y 1 WARP. Faltan curvas de inclinación y velocidad |
| LY-01 | Capas | Crítica | 🟡 | ✅ | ✅ | ✅ | 🟡 | [Protocolo](protocols/LY-01.md); 16 operaciones con deshacer y rehacer exactos; grupos en LY-03 |
| LY-02 | Máscaras | Crítica | 🟡 | ✅ | ✅ | ✅ | 🟡 | [Especificación](specs/LY-02-LY-03-masks-groups.md): máscara de capa en escala de grises (pintar, invertir, aplicar, eliminar) además de la de recorte; 5 pruebas WARP (7 casos), 1 referencia y autoprueba. Sin observación en iPad |
| LY-03 | Grupos de capas | Alta | 🟡 | ✅ | ✅ | ✅ | 🟡 | ídem: un nivel, composición aislada con opacidad, fusión y visibilidad; combinar, desagrupar, eliminar; formato v3. Prueba de propiedades de contigüidad (200 secuencias). Sin grupos anidados |
| SE-01 | Selección | Crítica | 🟡 | ✅ | ✅ | ✅ | 🟡 | [Especificación](specs/SE-01-selection.md): automática, a mano, rectángulo y elipse; añadir, restar e intersecar; invertir, difuminar, copiar y pegar. Todas las herramientas la respetan (comprobado píxel a píxel y con mutación). Faltan las selecciones guardadas y la pluma de selección |
| TR-01 | Transformar | Crítica | 🟡 | ✅ | ✅ | ✅ | 🟡 | [Especificación](specs/TR-01-transform.md): Libre, Uniforme (asas), Distorsionar (homografía) y Deformar (Bézier 4 × 4) dibujados con una malla de 2048 triángulos; Magnético. 8 pruebas de núcleo, 9 WARP, 2 referencias. Sin observación en iPad |
| LQ-01 | Licuar | Alta | 🟡 | ✅ | ✅ | ✅ | 🟡 | [Especificación](specs/LQ-01-liquify.md): Empujar, Girar a ambos lados, Pellizcar, Expandir, Reconstruir, Ajustar y Restablecer, con campo compuesto de forma exacta. 15 pruebas de núcleo, 3 WARP, 1 referencia. Faltan Cristales, Borde e impulso |
| DG-01 | Guías de dibujo y Asistente | Alta | 🟡 | ✅ | ✅ | ✅ | 🟡 | [Especificación](specs/DG-01-drawing-guides.md): cuadrícula 2D, isométrica y perspectiva de 1 a 3 puntos, con Asistente; se guarda en el `.trazo`. 8 pruebas de núcleo y 1 WARP |
| CO-01 | Composición y color | Crítica | 🟡 | 🟡 | ✅ | 🟡 | ⬜ | 16 modos de Direct2D. Falta medir la precisión cromática ([ADR-0004](adr/0004-color-and-compositing.md)) |
| HI-01 | Historial | Crítica | ✅ | ✅ | ✅ | ✅ | 🟡 | Pruebas en el núcleo y en LY-01. Falta cubrir las operaciones interrumpidas |
| FI-01 | Persistencia y recuperación | Crítica | ✅ | ✅ | ✅ | ✅ | ✅ | [Protocolo](protocols/FI-01.md); fallos inyectados en las 5 etapas del guardado |
| EX-01 | Interoperabilidad | Alta | ⬜ | ⬜ | 🟡 | ⬜ | ⬜ | PNG y JPEG; abre el formato web v1. Sin PSD ni `.procreate` |
| PF-01 | Rendimiento | Crítica | 🟡 | ✅ | ✅ | ✅ | 🟡 | [Especificación](specs/PF-01-performance.md): presupuestos por medición y `tools/Trazo.Benchmarks`. En una Intel Iris Xe con un lienzo de 4K y 20 capas, todas las mediciones cumplen el mínimo de 60 Hz; la composición también cumple el objetivo de 120 Hz. Cachés de autoguardado y de composición con pruebas de equivalencia. Falta la latencia de extremo a extremo con hardware de medida y medir en GPU dedicada y ARM |
| RC-01 | Recuperación tras fallos | Crítica | 🟡 | ✅ | ✅ | ✅ | 🟡 | [Especificación](specs/RC-01-recovery.md): diario de sesión con aviso al volver a abrir, recuperación tras perder la GPU y copia de emergencia. 7 pruebas de núcleo, 1 WARP, autoprueba y prueba con el proceso real (`tools/crash-test.ps1`). El disparo real de la pérdida de GPU no se puede provocar en pruebas |
| AN-01 | Animación | Media | 🟡 | ⬜ | ⬜ | ⬜ | ⬜ | Fuera de esta fase |

## Requisitos detallados de las áreas activas

### BR-01 — Entrada y trayectoria

| Req. | Requisito | Criterio de aceptación | Prueba |
|---|---|---|---|
| BR-01.1 | Las muestras se procesan en orden temporal | Una muestra con marca de tiempo anterior se descarta y se cuenta | `RejectsSamplesOutOfTemporalOrder` |
| BR-01.2 | Las muestras inválidas no corrompen el trazo | Las muestras NaN o infinitas se ignoran | `IgnoresNonFiniteSamples` |
| BR-01.3 | No hay discontinuidades | La distancia entre sellos consecutivos es ≤ 1,01 × el paso teórico, con StreamLine 0/0,3/0,7/1 en línea, círculo, zigzag y espiral | `NoGapsBetweenConsecutiveDabs` |
| BR-01.4 | La vista no desplaza el trazo | Con zoom de 0,25× a 16×, giro y desplazamiento, los mismos sellos (± 0,01 px) | `ViewTransformDoesNotMoveTheStroke` |
| BR-01.5 | StreamLine constante en pantalla | Radio = 26 px × StreamLine ÷ zoom | `StreamLineRadiusIsConstantOnScreen` |
| BR-01.6 | La frecuencia de muestreo no cambia la geometría | De 60 a 1000 Hz: línea idéntica (± 0,05 px); círculo dentro de la sagita teórica | `SamplingRateDoesNotChangeGeometry` |
| BR-01.7 | Estabilización controlada | Más StreamLine nunca aumenta la desviación media con temblor gaussiano | `StabilizationReducesTremorMonotonically` |
| BR-01.8 | Reproducibilidad | Misma semilla, mismos sellos; distinta semilla, sellos distintos | `SameSeedGivesIdenticalDabs` |
| BR-01.9 | Presión continua | El tamaño varía de forma monótona y sin saltos > 0,5 px entre sellos | `PressureIsInterpolatedContinuously` |
| BR-01.10 | Grabaciones reproducibles | Se pueden grabar trazos reales (Ctrl+Shift+R) y reproducirlos | `RecordingRoundTripsThroughJson` |
| BR-01.11 | Latencia | **Pendiente**: hay que definir el presupuesto y medirlo con lápiz real | — |

### FI-01 — Persistencia

| Req. | Requisito | Criterio | Prueba |
|---|---|---|---|
| FI-01.1 | Un guardado fallido no daña la versión anterior | Con fallos a mitad de escritura, disco lleno, al volcar, con corrupción y al reemplazar: original intacto | `FailedSaveKeepsPreviousVersionIntact` (5 casos) |
| FI-01.2 | Un guardado incompleto no se presenta como exitoso | Se lanza `SaveFailedException` con la etapa y un mensaje claro; la app lo muestra | ídem + `DiskFullIsReportedClearly` |
| FI-01.3 | Temporales seguros | No quedan `.tmp` tras un fallo | ídem |
| FI-01.4 | Recuperación | Si el principal está dañado se abre `.bak` y se avisa; si un guardado se interrumpió, se abre `.tmp` | `CorruptPrimaryRecoversFromBackup`, `InterruptedFirstSaveIsRecoveredFromTemporary` |
| FI-01.5 | Integridad | Cada capa es un PNG del tamaño del lienzo; se verifica releyendo antes de reemplazar | `RejectsLayerWithWrongSize`, `RejectsInvalidPng` |
| FI-01.6 | Guardar y reabrir con el motor real | Estructura idéntica; píxeles ± 1 nivel (redondeo PNG sin premultiplicar) | `SaveAndReopenPreservesDocument` |
| FI-01.7 | Archivo bloqueado por otro programa | Falla en la etapa Replace sin tocar el original | `InterruptedSaveDoesNotDamageExistingFile` |
| FI-01.8 | Pérdida máxima ante un cierre inesperado | **3 s** de trabajo (intervalo de autoguardado) | `tools/crash-test.ps1` (RC-01.9): pintar, esperar al autoguardado, matar el proceso y reabrir |

### LY-01 / HI-01 — Capas e historial

| Req. | Requisito | Criterio | Prueba |
|---|---|---|---|
| LY-01.1 | Cada operación deja un estado válido | Índice activo válido después de cada una de las 16 operaciones | `EveryOperationUndoesAndRedoesExactly` |
| HI-01.1 | Deshacer restaura exactamente | Estructura, píxeles de cada capa (SHA-256) e imagen compuesta idénticos en cada paso | ídem |
| HI-01.2 | Rehacer reproduce exactamente | Estado final idéntico tras rehacer todo | ídem |
| HI-01.3 | Un paso por acción discreta | Los interruptores (visible, recorte…) registran cada cambio; solo los deslizadores se agrupan | ídem (regresión de un error encontrado el 2026-10-24) |
| HI-01.4 | Límites de memoria | Se descartan los pasos más antiguos al superar el límite de pasos o de bytes | `RespectsStepAndMemoryLimits` |

### SE-01, LY-02 y LY-03 — Selección, máscaras y grupos

Los criterios de aceptación están en las especificaciones: [SE-01](specs/SE-01-selection.md#5-criterios-de-aceptación) (15) y [LY-02 / LY-03](specs/LY-02-LY-03-masks-groups.md#3-criterios-de-aceptación) (11). Todos tienen prueba automática.

### PF-01 y RC-01 — Rendimiento y recuperación

Los criterios de aceptación están en las especificaciones: [PF-01](specs/PF-01-performance.md#3-criterios-de-aceptación) (4) y [RC-01](specs/RC-01-recovery.md#3-criterios-de-aceptación) (9). Todos tienen prueba automática o medición.

### BR-04 y BR-06 — Mezcla húmeda y dinámica

Los criterios de aceptación están en las especificaciones: [BR-04](specs/BR-04-wet-mix.md#3-criterios-de-aceptación) (9) y [BR-06](specs/BR-06-dynamics.md#3-criterios-de-aceptación) (11). Todos tienen prueba automática.

### TR-01, LQ-01 y DG-01 — Transformar, Licuar y Guías

Los criterios de aceptación están en las especificaciones: [TR-01](specs/TR-01-transform.md#3-criterios-de-aceptación) (13), [LQ-01](specs/LQ-01-liquify.md#3-criterios-de-aceptación) (11) y [DG-01](specs/DG-01-drawing-guides.md#3-criterios-de-aceptación) (7). Todos tienen prueba automática.

## Errores encontrados por las pruebas (regresiones registradas)

| Fecha | Error | Prueba que lo cubre |
|---|---|---|
| 2026-10-24 | "Ocultar" y "mostrar" seguidos se fusionaban en un solo paso de historial: deshacer saltaba dos pasos | `EveryOperationUndoesAndRedoesExactly` (HI-01.3) |
| 2026-10-24 | La etiqueta "Opacidad por presión" modulaba en realidad el flujo (señalado por el inventario) | Corregida la etiqueta a "Flujo por presión"; opacidad y flujo separados el 2026-10-25 (`OpacityCapsAccumulationButFlowBuildsUp`, BR-06.8) |
| 2026-10-24 | El método de guardado anterior podía dejar un `.tmp` y no verificaba lo escrito | Sustituido por `ProjectSaver` (FI-01.1 a FI-01.5) |
| 2026-10-25 | La textura de grano «Lienzo» tenía discontinuidades cada 16 px | `GrainTilesSeamlessly` (BR-03.2) |
| 2026-10-25 | BR-02 calculaba el % de píxeles distintos sobre toda la imagen: un cambio solo en los extremos del trazo (afinado) pasaba sin detectarse | Métrica sobre píxeles con tinta; la pluma y el rotulador de pincel fallaron como debían y se actualizaron |
| 2026-10-25 | Una recta de la guía isométrica que rozaba una esquina del lienzo producía un tramo de longitud casi nula con ángulo arbitrario | `IsometricLinesGeometry` (DG-01.5): se descartan los tramos de menos de medio píxel |
| 2026-10-25 | Prueba débil: `PushComposes` (radio enorme, campo casi uniforme) no detectaba una fórmula de composición rota | Nueva `ComposedFieldEqualsSequentialWarps` (LQ-01.4b), comprobada con la mutación |
| 2026-10-25 | **Error de la 1.0 instalada (visto en su registro):** Win2D no resuelve `ms-appx:///` en una app sin empaquetar. La etiqueta de QuickShape perdía el fotograma y «Añadir texto» fallaba | `BundledFontsLoadWithoutPackage`, `NoWin2DFontUsesMsAppx` y la autoprueba (fuentes con `file:///`) |
| 2026-10-25 | Rendimiento: cada autoguardado codificaba todas las capas (9,4 s con 20 capas de 4K) | `IncrementalSaveMatchesFullEncode` (PF-01.2) y la medición PF-5d (0,5 s) |
| 2026-10-25 | Prueba mal planteada: el escenario de cierre forzado daba la señal de «guardado» al empezar a guardar y no al terminar, y el proceso podía morir a mitad del primer guardado | `RunCrashScenarioAsync` espera a `LastSaved`; el aviso al arrancar también cuenta un guardado interrumpido (temporal o copia) |
| 2026-10-25 | El script de publicación se bloqueaba al escribir el archivo de sumas SHA-256 | Corregido en `release.yml`; beta 0.2.0-beta.1 publicada y probada |
