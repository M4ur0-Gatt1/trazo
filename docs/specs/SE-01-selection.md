# SE-01 — Selección

**Estado:** en desarrollo · **Fecha:** 2026-10-25
**Código:** `src/Trazo.Core/Selection/`, `src/Trazo.Rendering/` · **Pruebas:** `SE01_*` en las dos suites

## 1. Referencia (Procreate, documentado)

Procreate ofrece selección **Automática** (por color, con umbral), **A mano** (lazo o polígono), **Rectángulo** y **Elipse**, con modos para añadir y restar, invertir, copiar y pegar, difuminar el borde (*Feather*), guardar y cargar selecciones y borrar el contenido. Con una selección activa, las herramientas solo afectan a la zona seleccionada, y Transformar mueve únicamente su contenido. El manual no documenta los algoritmos ni las unidades de difuminado.

## 2. Modelo de datos (decisiones de Trazo)

| Aspecto | Decisión | Motivo |
|---|---|---|
| Representación | **Máscara de cobertura de 8 bits por píxel** (`SelectionMask`, 0 = fuera, 255 = dentro), del tamaño del documento | Admite bordes suaves (antialias y difuminado) y operaciones booleanas exactas; se prueba sin GPU |
| Antialias de formas | Cobertura horizontal exacta × 4 submuestras verticales | Bordes suaves sin aliasing, deterministas |
| Lazo | Polígono cerrado con regla **par-impar** | Comportamiento esperado en lazos que se cruzan |
| Elipse | Polígono de 256 vértices inscrito en el rectángulo arrastrado | Error < 0,1 px para radios de hasta 2000 px |
| Automática | Relleno por inundación sobre la **imagen visible compuesta**, con umbral 0–255 y dilatación de 1 px (reutiliza `FloodFill`) | Igual criterio que el relleno de color |
| Combinación | Nueva / Añadir (máximo) / Restar (a·(1 − b)) / Intersecar (mínimo) | Fórmulas estándar para máscaras suaves |
| Difuminar | Desenfoque de caja separable en 3 pasadas (aproxima un gaussiano), radio en px | O(n) independiente del radio |
| Invertir | 255 − valor | — |
| Historial | Cada cambio de selección es un paso de deshacer (guarda la máscara anterior y la nueva) | Como en Procreate |

## 3. Efecto de la selección sobre las herramientas

**Regla única:** toda operación de píxeles sobre una capa (pincel, borrador, difuminar, relleno, ajustes, invertir, borrar contenido) se confirma así:

```
resultado = nuevo × m + anterior × (1 − m)        con m = cobertura de la selección
```

Se implementa en un solo punto: `LayerOperations.CommitPixels`. La vista previa en vivo aplica la misma mezcla.

| Operación | Comportamiento con selección |
|---|---|
| Pintar, borrar, difuminar, rellenar, ajustes, invertir | Solo cambian los píxeles seleccionados (con bordes suaves proporcionales) |
| Transformar | Levanta el contenido seleccionado; lo demás queda en su sitio |
| Copiar y pegar | Crea una capa nueva con el contenido seleccionado de la capa activa |
| Borrar contenido | Borra solo lo seleccionado |
| Sin selección | Todo funciona como antes, sin coste añadido |

## 4. Interfaz

- Botón **Seleccionar** en la barra superior. Abre una hoja inferior con el modo (Automática, A mano, Rectángulo, Elipse), la combinación (Nueva, Añadir, Restar, Intersecar), Umbral (automática), Difuminar, Invertir, Copiar y pegar, Borrar, Todo y Ninguna. Atajo **M**.
- En modo selección, el lápiz, el ratón o un dedo (si no está activo *Solo lápiz*) dibujan la forma; dos dedos siguen navegando.
- **Visualización:** la zona no seleccionada se oscurece y el borde se marca en cian.
- **Atajos:** Ctrl+A seleccionar todo, Ctrl+D deseleccionar, Ctrl+Shift+I invertir, Esc salir.
- Un toque sin forma con «Nueva» deselecciona. «Cancelar» deselecciona y sale; «Aplicar» sale conservando la selección, que sigue limitando las herramientas.
- Menú de la capa: **Seleccionar contenido** (la cobertura es el alfa de la capa).

## 5. Criterios de aceptación

| Req. | Criterio | Prueba |
|---|---|---|
| SE-01.1 | El área de un rectángulo es exacta, incluidos los bordes fraccionarios | `RectangleAreaIsExact` |
| SE-01.2 | El área de un polígono coincide con la fórmula del cordón (± 0,5 %) | `PolygonAreaMatchesShoelace` |
| SE-01.3 | El área de la elipse coincide con π·a·b (± 0,5 %) | `EllipseAreaMatchesFormula` |
| SE-01.4 | Antialias: los píxeles del borde tienen cobertura parcial; los interiores, 255 | `EdgesAreAntialiased` |
| SE-01.5 | Lazo que se cruza: regla par-impar | `SelfIntersectingLassoUsesEvenOdd` |
| SE-01.6 | Combinaciones según las fórmulas de la sección 2 | `CombineModes` |
| SE-01.7 | Invertir dos veces devuelve el original | `InvertIsInvolution` |
| SE-01.8 | Difuminar conserva el área (± 1 %) y suaviza el borde de forma monótona | `FeatherPreservesAreaAndSmoothsEdge` |
| SE-01.9 | Automática respeta el umbral y los bordes de color | `MagicWandFollowsColorRegion` |
| SE-01.10 | Con selección, pintar no cambia **ni un píxel** fuera de ella (pincel, borrador, difuminar, relleno, ajuste) | `ToolsOnlyAffectSelection` (WARP) |
| SE-01.11 | Transformar con selección mueve solo lo seleccionado | `TransformMovesOnlySelection` (WARP) |
| SE-01.12 | Copiar y pegar crea una capa con exactamente el contenido seleccionado | `CopyPasteSelection` (WARP) |
| SE-01.13 | Cambiar la selección se deshace y se rehace exactamente | `SelectionChangesAreUndoable` |
| SE-01.14 | «Seleccionar contenido» usa exactamente el alfa de la capa; una capa vacía no crea selección | `SelectLayerContentUsesAlpha` (WARP) |
| SE-01.15 | En la GPU real: relleno y borrado limitados a la selección y a su inversa | Autoprueba de la app |

## 6. Fuera de esta iteración

Guardar y cargar selecciones con nombre, la selección a mano por puntos (polígono tocando), la pluma de selección (pintar la selección con un pincel) y "Selección de color" por capa de referencia.
