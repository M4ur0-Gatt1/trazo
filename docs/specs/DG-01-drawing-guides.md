# DG-01 — Guías de dibujo y Asistente de dibujo

**Estado:** en desarrollo · **Fecha:** 2026-10-25
**Código:** `src/Trazo.Core/Geometry/DrawingGuide.cs` · **Pruebas:** `DG01_*` en las dos suites

## 1. Referencia (Procreate, documentado)

*Acciones › Lienzo › Guía de dibujo* muestra una **Cuadrícula 2D**, una **Isométrica**, una **Perspectiva** (1, 2 o 3 puntos de fuga, que se colocan tocando el lienzo) o la **Simetría**. Se ajustan el tamaño de la cuadrícula, el grosor, la opacidad y el color. Con **Asistente de dibujo** activado, los trazos se pegan a la guía: en la cuadrícula, a horizontales y verticales; en la isométrica, a sus tres direcciones; en la perspectiva, a líneas que van a los puntos de fuga. La guía se guarda con el lienzo.

## 2. Modelo (decisiones de Trazo)

| Aspecto | Decisión |
|---|---|
| Datos | `DrawingGuide`: tipo (Ninguna, Cuadrícula, Isométrica, Perspectiva), tamaño (px del lienzo), grosor (px de pantalla), opacidad, color, asistente, puntos de fuga (1 a 3) |
| Persistencia | En el manifiesto del `.trazo` (campo opcional `guide`; los lectores anteriores lo ignoran, por lo que no cambia la versión del formato). Cambiar la guía no ocupa pasos de deshacer |
| Valores por defecto | Tamaño 100 px, grosor 1 px, opacidad 40 %, cian; colores a elegir: cian, rojo anaranjado, negro y blanco |
| Cuadrícula 2D | Líneas cada `tamaño` px desde el origen del lienzo |
| Isométrica | Verticales y dos familias a ±30° de la horizontal, separadas `tamaño` px en horizontal |
| Perspectiva | Rayos desde cada punto de fuga (cada 6°); horizonte por los puntos 1 y 2 (o horizontal por el único punto); con 1 punto también horizontales y verticales; con 2, verticales |
| Asistente | Solo cambia la posición: la presión, la inclinación y el tiempo de cada muestra no se tocan (el motor sustituye X e Y). Al empezar un trazo se elige la dirección permitida más cercana al primer movimiento de más de 6 px de pantalla; hasta entonces el trazo no se mueve del punto inicial. Después, cada punto se proyecta sobre la recta que pasa por el inicio con esa dirección. En perspectiva, las direcciones son hacia cada punto de fuga (desde el inicio del trazo) más las de la fila anterior |
| Simetría | Sigue en *Guía de simetría*; puede combinarse con una guía (la simetría se aplica después del asistente) |

## 3. Criterios de aceptación

| ID | Criterio | Prueba |
|---|---|---|
| DG-01.1 | Cuadrícula: los trazos con asistente son exactamente horizontales o verticales según el primer movimiento | `GridAssistLocksAxis` |
| DG-01.2 | Isométrica: el trazo queda en una de las tres direcciones (0,01°) | `IsometricAssistUsesThreeAxes` |
| DG-01.3 | Perspectiva: cada punto del trazo está en la recta inicio → punto de fuga (± 1e-3 px) | `PerspectiveAssistConvergesToVanishingPoint` |
| DG-01.4 | Antes del umbral el trazo no se mueve y no hay dirección elegida | `AssistWaitsForThreshold` |
| DG-01.5 | Geometría de las líneas: número, separación y recorte al lienzo | `GridLinesGeometry`, `IsometricLinesGeometry`, `PerspectiveLinesGeometry` |
| DG-01.6 | La guía se guarda y se reabre; un archivo sin guía se abre sin guía | `GuideRoundTripsInManifest` |
| DG-01.7 | En la GPU: un trazo torcido con asistente pinta una línea recta | `AssistedStrokeIsStraight` (WARP) |

## 4. Fuera de esta iteración

Mover y girar la cuadrícula, la guía de simetría con asistente (ya existe la simetría sin él) y QuickShape condicionado por la guía.
