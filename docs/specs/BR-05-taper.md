# BR-05 — Afinado de extremos (Pressure Taper / Touch Taper)

**Estado:** implementado y probado (primera iteración) · **Fecha:** 2026-10-25
**Código:** `src/Trazo.Core/Brushes/Taper.cs`, `src/Trazo.Core/Strokes/StrokeBuilder.cs`, `src/Trazo.Rendering/BrushEngine.cs`
**Pruebas:** `tests/Trazo.Core.Tests/BR03_BR05_GrainTaperTests.cs`, `tests/Trazo.Rendering.Tests/BR03_BR05_GrainTaperRenderTests.cs`

## 1. Qué está documentado (Procreate 5.4)

Según el [inventario](../research/brush-studio-inventory.md#3-taper-afinado-de-extremos-13-subparámetros):

- Hay dos conjuntos: **Pressure Taper** (lápiz) y **Touch Taper** (dedo, que no tiene presión).
- Cada uno tiene un deslizador de dos tiradores (inicio y final), **Link Tip Sizes**, **Size** (brusquedad de la transición), **Opacity** (transparencia en el extremo) y **Tip** (punta fina o gruesa).
- Pressure Taper tiene además **Pressure** (afinado reactivo a la presión real) y **Tip Animation** (solo interfaz). También existe **Classic Taper**.

El manual **no** documenta las unidades de longitud, cómo se resuelve un trazo más corto que los dos afinados, ni cómo se afina el final antes de saber dónde termina el trazo.

## 2. Decisiones de Trazo (hipótesis a contrastar con un iPad)

| Aspecto | Decisión | Por qué |
|---|---|---|
| Unidad de longitud | **Diámetros del pincel** (0–30) | El afinado conserva su forma al cambiar el tamaño del pincel |
| Factor | f = min(fs, fe)^exponente, con fs = d / (Inicio·D) y fe = (L − d) / (Final·D), acotados a 0–1 | Continuo y monótono en cada rampa |
| Trazo más corto que los dos afinados | Manda el extremo más cercano (el **mínimo**), sin reescalar | Simple, predecible y comprobable |
| TipSize | Tamaño relativo en el extremo (0–1) | Equivale a "Tip" del manual (punta fina ↔ gruesa) |
| TipOpacity | Opacidad relativa en el extremo (0–1) | Equivale a "Opacity" |
| Shape | 0 cóncavo · 0,5 lineal · 1 convexo (exponente 3 … 1 … 1/3) | Equivale a "Size" (brusquedad) |
| Lápiz o dedo | `hasPressure` = el dispositivo es un lápiz → Pressure Taper; dedo o ratón → Touch Taper | Documentado |
| **Afinado final en vivo** | El `StrokeBuilder` retiene los sellos de los últimos Final × D px en una **cola provisional**. El lienzo la muestra **sin afinar** mientras se dibuja y la emite afinada al levantar el lápiz | El final no se conoce hasta levantar el lápiz; así no se redibuja todo el trazo |
| QuickShape | Las formas reciben los dos afinados, porque se conoce su longitud total | — |

**No implementado en esta iteración:** Pressure (afinado reactivo), Link Tip Sizes como control de la interfaz, Tip Animation y Classic Taper.

## 3. Requisitos y criterios de aceptación

| Req. | Requisito | Criterio | Prueba |
|---|---|---|---|
| BR-05.1 | Rampas de tamaño al inicio y al final | Primer y último sello = TipSize × D; centro = D; cada rampa monótona | `SizeRampsUpAtStartAndDownAtEnd` |
| BR-05.2 | Longitudes en diámetros | Alcanza el tamaño completo a Inicio·D ± 1,5 px del inicio y a Final·D ± 1,5 px del final | `TaperLengthIsMeasuredInDiameters` |
| BR-05.3 | Opacidad en la punta | Alfa del primer sello = TipOpacity; centro = 1 | `OpacityTapersToTipOpacity` |
| BR-05.4 | Trazo corto | Cada sello = TipSize + (1 − TipSize)·Factor(d, L − d) | `ShortStrokeUsesNearestEnd` |
| BR-05.5 | Lápiz frente a dedo | El lápiz usa Pressure Taper; el dedo, Touch Taper | `TouchAndPressureUseTheirOwnTaper`, `TouchTaperAppliesOnlyWithoutPressure` |
| BR-05.6 | Cola provisional | Mide Final·D; no se emite hasta levantar el lápiz; al levantarlo se emite entera y en orden | `TailIsHeldUntilEnd` |
| BR-05.7 | La cola se ve mientras se dibuja | En la vista previa la cola está a tamaño completo; tras levantar, afinada; el centro no cambia | `TailIsVisibleWhileDrawingAndTaperedOnRelease` |
| BR-05.8 | Sin afinado no cambia nada | Sin cola; todos los sellos a tamaño completo; las 16 pruebas BR-01 siguen pasando | `NoTaperLeavesStrokeUnchanged` + BR-01 |
| BR-05.9 | Forma del perfil | Shape 0 → por debajo de 0,5 a mitad de rampa; 0,5 → 0,5; 1 → por encima de 0,5 | `ShapeControlsProfileCurvature` |
| BR-05.10 | QuickShape afinado | Primer y último sello de la forma a TipSize × D | `QuickShapeGetsBothTapers` |
| BR-05.11 | Distancia monótona | La distancia recorrida crece sello a sello | `DistanceIsMonotonicAlongStroke` |
| BR-05.12 | Render real | Grosor en los extremos < ⅓ del grosor central | `TaperNarrowsBothEnds`; autoprueba en GPU real |
| BR-05.13 | Referencias visuales | Afinado con lápiz y con dedo | `ScenarioMatchesReference` (2 referencias) |

## 4. Pinceles con afinado

| Pincel | Lápiz (inicio / final / punta) | Dedo (inicio / final / punta) |
|---|---|---|
| Pluma de estudio | 1 / 2,5 / 0,05 | 1,5 / 4 / 0,05 |
| Rotulador de pincel | 1 / 2 / 0,05 | 2 / 4 / 0,05 |
| Pluma caligráfica | — | 1 / 2 / 0,2 |
| Bolígrafo técnico, Monolínea | — | 0,5 / 0,5 / 0,4 |
| Lápiz 6B, Lápiz HB | — | 1 / 2 / 0,3 |
| Tinta seca | — | 1 / 2 / 0,1 |

Con lápiz, la mayoría de pinceles se afinan ya por la presión real (`MinSizePressure`). El afinado táctil corrige que con el dedo los trazos salieran sin afinar.
