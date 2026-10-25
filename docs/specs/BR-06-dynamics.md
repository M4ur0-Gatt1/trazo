# BR-06 — Curvas de dinámica y opacidad separada del flujo

**Estado:** en desarrollo · **Fecha:** 2026-10-25
**Código:** `src/Trazo.Core/Brushes/DynamicsCurve.cs`, `BrushPreset`, `Stamping.cs`, `src/Trazo.Rendering/StrokeRasterizer.cs` · **Pruebas:** `BR06_*` en las dos suites

## 1. Referencia (Procreate, documentado)

En Brush Studio › *Apple Pencil*, la presión controla por separado el **tamaño**, la **opacidad** y el **flujo**, y cada relación puede tener su propia curva. La opacidad limita lo oscuro que llega a ser el trazo aunque los sellos se superpongan; el flujo es la tinta que deposita cada sello y sí se acumula. El inventario de Brush Studio (2026-10-24) señaló que Trazo llamaba «opacidad por presión» a algo que en realidad era flujo.

## 2. Modelo (decisiones de Trazo)

| Aspecto | Decisión |
|---|---|
| Curva | `DynamicsCurve`: de 2 a 8 puntos de control en [0,1]², con x estrictamente creciente; interpolación cúbica **monótona** de Fritsch-Carlson (no se sale del rango de los puntos vecinos ni oscila). Sin puntos = identidad |
| Tamaño | `tamaño = base × (mín + (1 − mín) × curvaTamaño(p))`, con mín = *Tamaño a presión mínima* |
| Flujo | `flujo = Flujo × (1 − k + k × curvaFlujo(p))`, con k = *Flujo por presión*. Cada sello deposita ese alfa y los sellos se acumulan |
| Opacidad | Nuevo. `tope(p) = 1 − k + k × curvaOpacidad(p)`, con k = *Opacidad por presión*. Cada sello deja en un búfer de **tope** su cobertura × tope, quedándose con el **máximo** por píxel; al componer, el trazo acumulado se multiplica por el tope. Donde el flujo satura (alfa 1), la opacidad final es exactamente el tope; con presión baja, superponer sellos no oscurece más |
| Tope en la CPU | Direct2D no tiene mezcla «máximo», y su mezcla «mínimo» se ignoró en WARP (una prueba lo mostró: el relleno se comportó como composición normal y las imágenes se rechazaron). El tope se calcula en la CPU (`OpacityCap`, muestreo bilineal de la máscara de la punta con giro y escala) y se sube a la GPU solo la zona que cambia. Solo existe si el pincel tiene *Opacidad por presión* > 0 |
| Compatibilidad | Con curvas identidad y *Opacidad por presión* = 0, los sellos y la imagen son idénticos a los de antes (las 21 referencias BR-02 no cambian por esto) |
| Persistencia | Las curvas y la opacidad por presión se guardan en las preferencias del pincel; las preferencias antiguas sin estos campos conservan los valores del pincel |

## 3. Criterios de aceptación

| ID | Criterio | Prueba |
|---|---|---|
| BR-06.1 | Sin puntos, la curva es la identidad | `EmptyCurveIsIdentity` |
| BR-06.2 | La curva pasa exactamente por sus puntos de control | `CurvePassesThroughControlPoints` |
| BR-06.3 | Con datos monótonos la curva es monótona y no se sale de [mín, máx] de los vecinos | `CurveIsMonotoneWithoutOvershoot` |
| BR-06.4 | Fuera de [0,1] la entrada se recorta; puntos desordenados o repetidos se rechazan | `CurveClampsAndValidates` |
| BR-06.5 | Con curvas identidad, tamaño y flujo coinciden con las fórmulas anteriores | `IdentityCurvesKeepPreviousDynamics` |
| BR-06.6 | Una curva de tamaño cambia el tamaño de los sellos según la fórmula | `SizeCurveShapesSize` |
| BR-06.7 | La opacidad por presión llega a los sellos como tope y no altera el flujo | `OpacityPressureSetsDabCap` |
| BR-06.8 | En la GPU: con opacidad por presión y presión 0,3, un trazo con muchos sellos superpuestos no pasa del 30 % (± 2 %); con flujo por presión en su lugar, la misma acumulación supera el 90 % | `OpacityCapsAccumulationButFlowBuildsUp` (WARP) |
| BR-06.9 | Las curvas sobreviven a guardar las preferencias | `CurveRoundTripsThroughJson` |
| BR-06.10 | El tope es el máximo, no la suma: repetir un sello no lo sube, uno más fuerte sí y uno más débil no lo baja | `OpacityCapTakesMaximumNotSum` |
| BR-06.11 | El tope solo cambia dentro del diámetro de la punta y del rectángulo que devuelve; la punta redonda no depende del giro | `OpacityCapIsLocalAndReportsItsRectangle` |

## 4. Fuera de esta iteración

Curvas para la inclinación y la velocidad, y la curva global de presión editable por puntos (hoy es una gamma).
