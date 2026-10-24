# BR-03 — Grano independiente (Moving / Texturized)

**Estado:** implementado y probado (primera iteración) · **Fecha:** 2026-10-25
**Código:** `src/Trazo.Core/Brushes/Grain.cs`, `src/Trazo.Rendering/StrokeRasterizer.cs`
**Pruebas:** `tests/Trazo.Core.Tests/BR03_BR05_GrainTaperTests.cs`, `tests/Trazo.Rendering.Tests/BR03_BR05_GrainTaperRenderTests.cs`

## 1. Qué está documentado (Procreate 5.4)

Según el [inventario](../research/brush-studio-inventory.md#5-grain-grano-16-subparámetros):

- El grano es una textura que se repite en mosaico **independiente de la forma** de la punta.
- **Moving:** el grano se arrastra con el trazo y deja marcas estriadas. **Texturized:** queda fijo detrás del trazo, como una plantilla.
- **Movement** (solo Moving): bajo, el grano se emborrona; alto, "rueda" como en Texturized.
- También existen Scale, Zoom (Cropped ↔ Follow Size), Rotation, Depth, Offset Jitter, Brightness, Contrast y Blend Mode, además de Depth Minimum, Depth Jitter, Filtering, el editor y la importación de granos.

El manual **no** documenta rangos, unidades ni fórmulas.

## 2. Decisiones de Trazo (hipótesis a contrastar con un iPad)

| Parámetro | Rango en Trazo | Modelo | Origen |
|---|---|---|---|
| Mode | Off / Moving / Texturized | — | Documentado |
| Kind | Papel, Lienzo, Carboncillo, Ruido, Esponja | Texturas de 256 × 256 generadas por código y **periódicas** (repetibles sin costuras) | Decisión propia (Procreate trae más de 100 texturas importables) |
| Scale | 0,1–4 | Período = 256 px × Scale | Decisión propia |
| Zoom / FollowSize | sí / no | Con Moving + FollowSize: período = 2 × diámetro × Scale | Decisión propia; el comportamiento documentado es "sigue el tamaño" |
| Depth | 0–1 | alfa' = alfa · (1 − Depth · (1 − g)), con g = luminancia del grano (0–1) | Decisión propia |
| Movement | 0–1 | Origen del grano = posición del sello × (1 − Movement) | Decisión propia que reproduce lo documentado: 0 viaja con el sello, 1 anclado al lienzo |
| Rotation | −1…1 | Giro del grano = Rotation × dirección de avance | Decisión propia, inspirada en el rango documentado (−100 % … Follow Stroke) |
| Offset Jitter | sí / no | Desplazamiento aleatorio por trazo, con la semilla del trazo | Decisión propia |
| Brightness / Contrast | −1…1 / 0…2 | Se aplican a la textura antes de usarla | Decisión propia |

### Dónde se aplica cada modo

- **Texturized:** al trazo **ya acumulado** (búfer del trazo × máscara de grano anclada al lienzo), al previsualizar y al confirmar. Así la textura no se satura aunque los sellos se superpongan, que era la causa de que los lápices salieran "demasiado limpios" (brecha detectada en BR-02).
- **Moving:** en **cada sello** (punta × grano). Se usa un único grafo de efectos de Direct2D por trazo y en cada sello solo se actualizan sus transformaciones.

### Comportamiento observado al implementar (registrado)

Con flujo 1 y sellos densos (espaciado 0,05), el grano **Moving** se acumula hasta saturar y desaparece: cada píxel recibe unos 20 sellos con valores de grano distintos. El grano en movimiento solo se aprecia con **flujo bajo o espaciado amplio**. Hay que comprobar si Procreate se comporta igual (pregunta abierta).

## 3. Requisitos y criterios de aceptación

| Req. | Requisito | Criterio | Prueba |
|---|---|---|---|
| BR-03.1 | Texturas deterministas con contraste útil | Mismos bytes en cada generación; rango ≥ 150 niveles | `GrainIsDeterministicAndUsesFullRange` |
| BR-03.2 | Mosaico sin costuras | El salto en el borde 255 → 0 ≤ el mayor salto entre columnas o filas vecinas del interior | `GrainTilesSeamlessly` (**detectó un fallo real** en la textura Lienzo; corregido) |
| BR-03.3 | Brillo y contraste monótonos | Media y desviación típica se mueven en el sentido esperado | `BrightnessAndContrastAreMonotonic` |
| BR-03.4 | Fórmula de profundidad | Depth 0 → factor 1; Depth 1 → factor g | `DepthBlendsGrainIntoAlpha` |
| BR-03.5 | Depth 0 equivale a no tener grano | Bytes idénticos, en Moving y en Texturized | `ZeroDepthIsIdenticalToNoGrain` |
| BR-03.6 | El grano añade textura medible | σ del alfa en el centro del trazo: < 1 sin grano, > 20 con grano | `GrainAddsMeasurableTexture` |
| BR-03.7 | Más Depth no aumenta la cobertura | Alfa medio monótono no creciente | `DepthMonotonicallyReducesCoverage` |
| BR-03.8 | Texturized anclado al lienzo | Dos sellos en posiciones distintas coinciden sin desplazar (> 95 %) y no desplazados (< 60 %) | `FullMovementAndTexturizedAnchorGrainToCanvas` |
| BR-03.9 | Moving con Movement 0 viaja con el sello | Coinciden desplazados (> 95 %) y no sin desplazar (< 60 %) | `MovingGrainTravelsWithTheStroke` |
| BR-03.10 | Moving con Movement 1 se ancla al lienzo | Igual que BR-03.8 | ídem |
| BR-03.11 | El período sigue el tamaño solo con Moving + FollowSize | Período ×2 al duplicar el diámetro; fijo en Texturized | `PeriodFollowsSizeOnlyWhenMoving` |
| BR-03.12 | Referencias visuales | Texturized (papel, lienzo, carboncillo) y Moving (Movement 0 y 1) | `ScenarioMatchesReference` (5 referencias revisadas) |
| BR-03.13 | Funciona en la GPU real y en la vista previa | Autoprueba de la app | `--selftest` |

## 4. Fuera de esta iteración

Depth Minimum, Depth Jitter, Blend Mode del grano (hoy solo multiplica el alfa), Grain Filtering, editor e importación de granos, y grano 3D. Las preguntas abiertas están en el inventario, sección 17.

## 5. Pinceles que usan grano

| Pincel | Modo | Textura | Depth | Scale |
|---|---|---|---|---|
| Lápiz 6B | Texturized | Papel | 0,75 | 0,12 |
| Lápiz HB | Texturized | Papel | 0,6 | 0,12 |
| Carboncillo | Texturized | Carboncillo | 0,8 | 0,4 |
| Tinta seca | Texturized | Ruido | 0,5 | 0,3 |
| Gouache | Texturized | Lienzo | 0,35 | 0,4 |
| Óleo | Texturized | Lienzo | 0,2 | 0,5 |
| Acuarela | Texturized | Papel | 0,35 | 0,8 |
| Tiza | Texturized | Papel | 0,7 | 0,5 |
| Grano | Texturized | Ruido | 0,9 | 0,3 |

Los valores se eligieron revisando a ojo las referencias de BR-02. Ajustar un pincel actualiza su referencia de forma deliberada.
