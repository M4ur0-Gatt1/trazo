# CO-02 — Panel de color: disco, armonía, valor y paletas

**Estado:** implementado · **Fecha:** 2026-10-27
**Código:** `src/Trazo.Core/Colors/Harmony.cs` y `src/Trazo.Core/Colors/PaletteFiles.cs` (sin GPU), `src/Trazo.App/Views/StudioView.Color.cs` (panel) · **Pruebas:** `CO02_*` en `Trazo.Core.Tests` y la autoprueba

## 1. Referencia

El panel de color de Procreate tiene cinco vistas (disco, clásico, armonía, valor y paletas), y las paletas se importan y exportan en `.swatches` (Procreate) y se pueden traer de Adobe en `.ase`. Muchos artistas tienen sus paletas en esos formatos, así que importarlas es lo que hace que puedan trabajar con sus colores de siempre. Hasta ahora Trazo tenía el selector de color de Windows, el historial, una paleta fija y una propia.

## 2. Decisiones

| Aspecto | Decisión |
|---|---|
| Vistas | Cuatro pestañas: **Disco** (anillo de tono con saturación y brillo, el selector de Windows), **Armonía**, **Valor** (deslizadores HSB y RGB y código hexadecimal) y **Paletas**. Debajo de todas, el historial de colores recientes. Se recuerda la última pestaña |
| Armonía | Sobre la rueda de tono HSB: **complementario** (180°), **complementario dividido** (±150°), **análogo** (±30°), **triádico** (±120°) y **tetrádico** (rectángulo: 60°, 180°, 240°). Los colores ligados conservan la saturación y el brillo del principal. El disco muestra el principal y los ligados; tocar o arrastrar en el disco mueve el principal; un deslizador cambia el brillo; tocar un color ligado lo elige |
| Paletas | Varias paletas con nombre (hasta 256 colores cada una): crear, renombrar, eliminar, añadir el color actual, quitar un color (clic derecho) y elegir la **predeterminada**, que es la que se abre primero. La paleta propia que ya existía pasa a ser «Mi paleta» sin perder colores; la fija pasa a ser la paleta «Trazo» |
| Importar | `.swatches` de Procreate (ZIP con `Swatches.json`: nombre y colores en HSB de 0 a 1; los huecos vacíos se saltan) y `.ase` de Adobe (RGB, CMYK, gris y Lab; cada grupo pasa a ser una paleta y los colores sueltos van a una con el nombre del archivo). CMYK y Lab se convierten a sRGB de forma aproximada y se avisa |
| Exportar | `.swatches` (se abre en Procreate) y `.ase` (RGB, se abre en Photoshop, Illustrator, Krita y Clip Studio) |
| Errores | Un archivo dañado o de otro formato lanza `InvalidDataException`, nunca otra cosa, y se avisa con un mensaje claro |

## 3. Criterios de aceptación

| ID | Criterio | Prueba |
|---|---|---|
| CO-02.1 | Cada armonía da el número de colores y los ángulos de su definición, conservando saturación y brillo, con el tono siempre en [0, 360) | `HarmoniesFollowTheirAngles` |
| CO-02.2 | `.swatches`: escribir y leer devuelve el nombre y los colores (± 1 nivel por el paso a HSB); se leen archivos con huecos `null` y con valores fuera de rango | `SwatchesRoundTripAndProcreateQuirks` |
| CO-02.3 | `.ase`: escribir y leer devuelve los colores exactos (RGB en coma flotante); se leen grupos, colores sueltos, CMYK, gris y Lab con los valores esperados | `AseRoundTripGroupsAndModels` |
| CO-02.4 | Ningún archivo truncado o con bytes cambiados provoca otra cosa que `InvalidDataException`; nombres y número de colores quedan acotados | `MalformedPaletteFilesFailCleanly` |
| CO-02.5 | Las paletas de las preferencias anteriores pasan a las nuevas sin perder colores, y la predeterminada sobrevive a renombrar y eliminar otras | `OldPaletteMigratesAndDefaultIsKept` |
| CO-02.6 | En la app real: el panel se abre en cada pestaña, la armonía cambia el color, y una paleta exportada en los dos formatos se vuelve a importar igual | Autoprueba en GPU real (4 comprobaciones; deja las paletas del usuario como estaban) y capturas de armonía, valor y paletas en los dos idiomas |
