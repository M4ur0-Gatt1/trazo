# LY-04 — Capa de referencia

**Estado:** implementado · **Fecha:** 2026-10-27
**Código:** `LayerProperties.Reference` (núcleo), `PaintDocument.FillSource` y `LayerOperations.SetReference` (motor), menú de la capa (app) · **Pruebas:** `LY04_*` en las dos suites y la autoprueba

## 1. Referencia

En Procreate, una capa marcada como **Referencia** hace que el relleno por arrastre de color y la selección automática miren solo esa capa: se entinta en una capa, se marca como referencia y se colorea en otra capa debajo sin que los colores ya puestos frenen el relleno. Es el flujo habitual para colorear línea.

## 2. Decisiones

| Aspecto | Decisión |
|---|---|
| Cuántas | Como mucho una capa de referencia; marcar otra quita la marca a la anterior |
| Qué cambia | El **relleno** (arrastrar el color o la herramienta de relleno) y la **selección automática** calculan la zona sobre los píxeles de la capa de referencia sola, en lugar de sobre la imagen aplanada. El relleno se sigue pintando en la capa activa |
| Visibilidad | La referencia funciona aunque esa capa esté oculta (para colorear sin ver la línea, como en Procreate) |
| Sin referencia | Igual que antes: la zona se calcula sobre la imagen aplanada |
| Si la activa es la referencia | El relleno también la usa (se rellena la línea consigo misma, como antes) |
| Historial | Marcar y desmarcar es un paso de deshacer. Eliminar la capa de referencia deja el documento sin referencia; deshacerlo la recupera |
| Archivo | Campo opcional `reference` en el manifiesto de `.trazo` (las versiones anteriores lo ignoran). En PSD no hay equivalente: se exporta como capa normal |
| Interfaz | «Referencia» en el menú de la capa, y la etiqueta «referencia» bajo su nombre en el panel de capas |

## 3. Criterios de aceptación

| ID | Criterio | Prueba |
|---|---|---|
| LY-04.1 | El manifiesto guarda y lee la marca; un archivo sin el campo se lee sin referencia; con dos marcadas (archivo editado a mano) se queda solo la primera | `ReferenceFlagRoundTripsInTrazo` |
| LY-04.2 | Marcar una capa quita la marca a la anterior; marcar y desmarcar se deshacen en un paso; eliminar la capa de referencia y deshacer la recupera | `OnlyOneReferenceAndUndo` (WARP) |
| LY-04.3 | Con una línea cerrada en la capa de referencia y una mancha de otro color dentro en otra capa, el relleno en una tercera capa cubre todo el interior (la mancha no lo frena); sin referencia, la mancha deja un hueco | `FillUsesReferenceLayer` (WARP) |
| LY-04.4 | La referencia oculta también delimita; la selección automática usa la referencia | `HiddenReferenceAndAutoSelection` (WARP) |
| LY-04.5 | En la app real: marcar la referencia, rellenar y deshacer | Autoprueba en GPU real |
