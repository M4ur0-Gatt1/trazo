# Matriz de requisitos

Estado al **2026-10-24** (commit de esta revisión). Las prioridades son propuestas de ingeniería, no clasificaciones de Procreate.

Leyenda de las puertas: ✅ superada con evidencia · 🟡 parcial · ⬜ pendiente.
Columnas: **Inv**estigación · **Esp**ecificación · **Impl**ementación · **Pru**ebas · **Apr**obación.

## Resumen por área

| ID | Área | Prioridad | Inv | Esp | Impl | Pru | Apr | Evidencia o bloqueo |
|---|---|---|---|---|---|---|---|---|
| BR-01 | Entrada del lápiz y trayectoria | Crítica | 🟡 | ✅ | ✅ | ✅ | 🟡 | [Protocolo](protocols/BR-01.md); 16 pruebas con entrada sintética. Falta validar con lápiz real y medir la latencia (PF-01) |
| BR-02 | Reproducibilidad del motor de pinceles | Crítica | ✅ | ✅ | ✅ | ✅ | ✅ | [Protocolo](protocols/BR-02.md); 21 referencias revisadas; prueba de mutación superada |
| BR-03 | Texturas y grano | Alta | 🟡 | ⬜ | ⬜ | ⬜ | ⬜ | El grano va dentro de la punta; no hay modos Moving ni Texturized ([inventario](research/brush-studio-inventory.md)) |
| BR-04 | Mezcla húmeda | Crítica | 🟡 | ⬜ | ⬜ | ⬜ | ⬜ | Sin implementar; el modelo de mezcla requiere observación |
| BR-05 | Afinado (taper) | Alta | 🟡 | ⬜ | ⬜ | ⬜ | ⬜ | Sin implementar; con el dedo el trazo no se afina |
| BR-06 | Curvas de dinámica por parámetro | Alta | 🟡 | ⬜ | 🟡 | ⬜ | ⬜ | Solo hay gamma global y dos relaciones lineales |
| LY-01 | Capas | Crítica | 🟡 | ✅ | ✅ | ✅ | 🟡 | [Protocolo](protocols/LY-01.md); 16 operaciones con deshacer y rehacer exactos. Faltan los grupos |
| LY-02 | Máscaras | Crítica | ⬜ | ⬜ | 🟡 | ⬜ | ⬜ | Solo existe la máscara de recorte; faltan las máscaras de capa |
| CO-01 | Composición y color | Crítica | 🟡 | 🟡 | ✅ | 🟡 | ⬜ | 16 modos de Direct2D. Falta medir la precisión cromática ([ADR-0004](adr/0004-color-and-compositing.md)) |
| HI-01 | Historial | Crítica | ✅ | ✅ | ✅ | ✅ | 🟡 | Pruebas en el núcleo y en LY-01. Falta cubrir las operaciones interrumpidas |
| FI-01 | Persistencia y recuperación | Crítica | ✅ | ✅ | ✅ | ✅ | ✅ | [Protocolo](protocols/FI-01.md); fallos inyectados en las 5 etapas del guardado |
| EX-01 | Interoperabilidad | Alta | ⬜ | ⬜ | 🟡 | ⬜ | ⬜ | PNG y JPEG; abre el formato web v1. Sin PSD ni `.procreate` |
| PF-01 | Rendimiento | Crítica | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | Se registran tiempos con WARP, pero aún no hay presupuestos ni medición en GPU real |
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
| FI-01.8 | Pérdida máxima ante un cierre inesperado | **3 s** de trabajo (intervalo de autoguardado) | Especificado; prueba de cierre forzado pendiente |

### LY-01 / HI-01 — Capas e historial

| Req. | Requisito | Criterio | Prueba |
|---|---|---|---|
| LY-01.1 | Cada operación deja un estado válido | Índice activo válido después de cada una de las 16 operaciones | `EveryOperationUndoesAndRedoesExactly` |
| HI-01.1 | Deshacer restaura exactamente | Estructura, píxeles de cada capa (SHA-256) e imagen compuesta idénticos en cada paso | ídem |
| HI-01.2 | Rehacer reproduce exactamente | Estado final idéntico tras rehacer todo | ídem |
| HI-01.3 | Un paso por acción discreta | Los interruptores (visible, recorte…) registran cada cambio; solo los deslizadores se agrupan | ídem (regresión de un error encontrado el 2026-10-24) |
| HI-01.4 | Límites de memoria | Se descartan los pasos más antiguos al superar el límite de pasos o de bytes | `RespectsStepAndMemoryLimits` |

## Errores encontrados por las pruebas (regresiones registradas)

| Fecha | Error | Prueba que lo cubre |
|---|---|---|
| 2026-10-24 | "Ocultar" y "mostrar" seguidos se fusionaban en un solo paso de historial: deshacer saltaba dos pasos | `EveryOperationUndoesAndRedoesExactly` (HI-01.3) |
| 2026-10-24 | La etiqueta "Opacidad por presión" modulaba en realidad el flujo (señalado por el inventario) | Corregida la etiqueta a "Flujo por presión"; falta separar opacidad y flujo (BR-06) |
| 2026-10-24 | El método de guardado anterior podía dejar un `.tmp` y no verificaba lo escrito | Sustituido por `ProjectSaver` (FI-01.1 a FI-01.5) |
