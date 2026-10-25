# TR-01 — Transformar

**Estado:** en desarrollo · **Fecha:** 2026-10-25
**Código:** `src/Trazo.Core/Geometry/Deformation.cs`, `src/Trazo.Rendering/ImageTools.cs` (`TransformSession`), `src/Trazo.Rendering/MeshRenderer.cs` · **Pruebas:** `TR01_*` en las dos suites

## 1. Referencia (Procreate, documentado)

Procreate tiene cuatro modos de transformar: **Libre** (escala no proporcional), **Uniforme** (proporcional), **Distorsionar** (cada esquina por separado, que da perspectiva) y **Deformar** (*Warp*: una malla que se dobla como una tela). Además: **Magnético** (ajusta el giro y el movimiento a valores redondos), voltear, rotar 45°, ajustar a la pantalla, interpolación (vecino más próximo, bilineal, bicúbica) y restablecer. El manual no documenta las fórmulas de la malla ni los umbrales magnéticos.

## 2. Modelo (decisiones de Trazo)

La transformación final es **F(p) = M · D(p)**: una deformación D del rectángulo del contenido y, encima, una transformación afín M.

| Modo | D | Qué edita el usuario |
|---|---|---|
| Libre / Uniforme | La que hubiera (identidad al empezar) | M: arrastrar mueve; dos dedos escalan y giran; las asas de esquina y de lado escalan desde la esquina o el lado opuesto (Uniforme mantiene la proporción); el asa superior gira |
| Distorsionar | **Homografía** de las 4 esquinas | Arrastrar cada esquina |
| Deformar | **Parche de Bézier bicúbico** de 4 × 4 puntos de control | Arrastrar cualquiera de los 16 puntos; arrastrar dentro de la malla mueve todo |

- **Cambiar de modo conserva lo hecho:** M se mantiene y solo cambia el tipo de D. Al entrar en Distorsionar, las esquinas son las de D; al entrar en Deformar, los 16 puntos son D en una cuadrícula de tercios (exacto si D es afín; aproximado si es una perspectiva); al volver a Libre o Uniforme se conserva D y se sigue editando M encima. De Deformar a Distorsionar solo se conservan las esquinas.
- **Puntos y asas:** los puntos de Distorsionar y Deformar se editan en el espacio anterior a M, así que mover y girar con dos dedos sigue funcionando en todos los modos. `SetCorners` coloca las 4 esquinas a la vez y valida el cuadrilátero entero.
- **Render:** si D es la identidad, Direct2D aplica M directamente (camino de siempre, sin cambios en las referencias existentes). Si no, se dibuja una **malla de 32 × 32 celdas** (2048 triángulos), cada triángulo relleno con la imagen mediante una transformación afín y sin antialias, para que triángulos vecinos no dejen costuras ni se solapen.
- **Error de la malla:** en una perspectiva, la aproximación afín por triángulo se separa de la homografía exacta menos de 0,5 px en lienzos de hasta 4096 px.
- **Homografía degenerada** (esquinas cruzadas, cóncavas, colineales o con menos de 1 px² de área): se rechaza el arrastre y la esquina se queda en su última posición válida.
- **Magnético** (activado por defecto): el giro se ajusta a múltiplos de 15° si está a menos de 3°; al mover con un dedo, si el desplazamiento está a menos de 6° de la horizontal o la vertical, se bloquea ese eje; el centro del contenido se ajusta al centro del lienzo si está a menos de 8 px de pantalla. Las asas de esquina en Libre ajustan la escala a 100 % si está a menos del 3 %.
- **Selección:** como hasta ahora (SE-01), se transforma solo lo seleccionado y la selección se transforma con la misma F.

## 3. Criterios de aceptación

| ID | Criterio | Prueba |
|---|---|---|
| TR-01.1 | La homografía lleva exactamente las 4 esquinas de origen a las de destino (± 1e-3 px) y su inversa deshace la ida | `HomographyMapsCornersExactly`, `HomographyInverseRoundTrips` |
| TR-01.2 | Una homografía de esquinas colineales o cruzadas se rechaza | `DegenerateQuadIsRejected` |
| TR-01.3 | Un parche de Bézier con puntos en los tercios de un afín reproduce el afín (± 1e-3 px) | `BezierPatchReproducesAffine` |
| TR-01.4 | Cambiar de modo no mueve el contenido (Libre → Distorsionar → Deformar → Libre) | `ModeSwitchKeepsMapping` |
| TR-01.5 | Magnético: giro a 15°, bloqueo de eje y centro, con sus umbrales; fuera del umbral no ajusta | `MagneticsSnapsWithinThreshold` |
| TR-01.6 | Asas en Libre y Uniforme: la esquina opuesta queda fija; Uniforme conserva la proporción | `CornerHandleKeepsOppositeCorner` |
| TR-01.7 | Distorsionar con las esquinas sin mover y Deformar sin mover dan la imagen original (diferencia ≤ 1 nivel) | `IdentityDeformationIsLossless` (WARP) |
| TR-01.8 | Una malla que representa un afín coincide con el camino afín de Direct2D | `MeshMatchesAffinePath` (WARP) |
| TR-01.9 | Una perspectiva coincide con el remuestreo exacto de referencia (error medio < 2 niveles, máximo en el borde) | `PerspectiveMatchesReference` (WARP) |
| TR-01.10 | Sin costuras ni solapes: una capa con alfa 128 deformada conserva alfa 128 ± 1 en todos los píxeles interiores | `WarpHasNoSeams` (WARP) |
| TR-01.11 | Con selección, Distorsionar solo cambia lo seleccionado y mueve la selección | `DistortMovesOnlySelection` (WARP) |
| TR-01.12 | Aplicar se deshace y rehace exactamente | `TransformModesUndoRedo` (WARP) |
| TR-01.13 | Referencias revisadas a ojo de una perspectiva y una deformación | `DistortAndWarpMatchReference` (WARP) |

## 4. Fuera de esta iteración

Rotar 45° con un botón propio, la malla avanzada de Deformar (más de 4 × 4 puntos), la interpolación bicúbica en el camino de malla (usa bilineal) y la vista de las asas de lado en Distorsionar.
