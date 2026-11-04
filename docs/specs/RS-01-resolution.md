# RS-01 — Resolución del lienzo

**Estado:** implementado · **Fecha:** 2026-11-04
**Código:**
- `CanvasLimits.ImportScale` y `CanvasLimits.CanScale` (`src/Trazo.Core/Documents/DocumentModel.cs`)
- `ZoomFilters` (`src/Trazo.Core/Geometry/ViewTransform.cs`)
- `DrawingGuide.Scaled`
- `PaintDocument.Resampled`
- `StudioView.Resolution.cs`
- `MainWindow.ImportImageAsync`

**Pruebas:** `RS01_*` (núcleo y WARP) y autoprueba.

## 1. Problema

Informe del usuario (2026-11-04): «se ve demasiado pixelado lo que dibujo». Trabaja sobre una historieta importada de 1536×1024 px, a la que bajó la opacidad para usarla de referencia, y dibuja en otra capa. Hay dos causas:

1. **El lienzo tenía el tamaño de la imagen.** Al acercarse a una imagen pequeña, cada píxel ocupa varios de la pantalla, y lo que se dibuja encima también.
2. **A partir de un zoom de 2,5, el lienzo se mostraba píxel a píxel** (vecino más cercano), sin contar la escala de la pantalla. Con una pantalla al 150 %, ese zoom ya es casi 4 píxeles físicos por píxel del lienzo, en escalones.

## 2. Decisiones

**Importar una imagen**
- El lienzo se agranda por un factor entero (×2, ×3 o ×4) hasta que el lado largo llega a **3000 px**, sin pasar de los límites (16384 px de lado, 64 M px de área).
- La imagen se escala con interpolación cúbica.
- Se avisa: «La imagen (A×B) se agrandó a C×D…».
- El factor es entero para que el escalado sea uniforme. 1536×1024 pasa a 3072×2048.
- Los PSD no se tocan: traen su propia resolución de trabajo.

**Duplicar la resolución** (*Acciones › Lienzo › Duplicar la resolución (A×B)…*), para obras que ya existen:
- Se crea una copia del documento al doble de tamaño. Capas y máscaras se escalan con interpolación cúbica.
- Se conservan propiedades, grupos, fondo, capa activa y guía; la guía se escala.
- Se mantiene la vista (zoom a la mitad).
- Se pide confirmación, porque el historial de deshacer se vacía. Antes se guarda el estado actual, así que la versión anterior queda en el `.bak` hasta el siguiente guardado.
- Si el lienzo duplicado pasaría de los límites, o no cabrían todas sus capas, se avisa y no se hace nada.

**Escalado en pantalla**
- Se cuentan los píxeles **físicos** de la pantalla por píxel del lienzo (zoom × escala de la pantalla):

  | Píxeles físicos por píxel del lienzo | Escalado |
  |---|---|
  | Menos de 0,5 | Cúbico |
  | De 0,5 a menos de 6 | Lineal |
  | 6 o más | Píxel a píxel, para ver los píxeles reales |

## 3. Criterios de aceptación

| # | Criterio | Prueba |
|---|---|---|
| RS-01.1 | Factor de importación: 1536×1024 → ×2, 1024×768 → ×3, 640×480 → ×4, 3000 px o más → ×1 | `RS01_1_ImportScale` (7 casos) |
| RS-01.2 | El factor nunca pasa de 4 ni de los límites | `RS01_2_ImportScaleStaysWithinLimits` |
| RS-01.3 | `CanScale` comprueba lado, área y número de capas | `RS01_3_CanScaleChecksSideAreaAndLayers` |
| RS-01.4 | La guía se escala con el lienzo (separación y puntos de fuga; el grosor no) | `RS01_4_GuideScalesWithCanvas` |
| RS-01.5 | El escalado en pantalla cuenta píxeles físicos | `RS01_5_ZoomFilterCountsScreenPixels` (7 casos) |
| RS-01.6 | Duplicar conserva contenido (en el doble de coordenadas), máscaras, propiedades, grupos, fondo, guía y capa activa | `DoublingKeepsContentPropertiesGroupsAndMasks` (WARP) |
| RS-01.7 | Duplicar más allá de los límites se rechaza | `RefusesBeyondLimits` (WARP) |
