# EX-01 — Importar y exportar PSD

**Estado:** implementado · **Fecha:** 2026-10-26
**Código:** `src/Trazo.Core/IO/Psd.cs` (formato, sin dependencias), `src/Trazo.Rendering/PsdInterop.cs` (con el motor), `tools/psd-interop/` (verificación cruzada) · **Pruebas:** `EX01_*` en las dos suites y `tools/psd-interop`

## 1. Referencia

PSD es el formato de Photoshop y el de intercambio más común entre aplicaciones de dibujo (Procreate, Krita, Clip Studio, Affinity y Photopea lo leen y escriben). Adobe publica su estructura en la *Adobe Photoshop File Formats Specification*: cabecera, datos de modo de color, recursos de imagen, información de capas y máscaras (registros de capa y datos de canal) e imagen compuesta final.

## 2. Alcance y decisiones

| Aspecto | Exportar | Importar |
|---|---|---|
| Versión | PSD (versión 1, hasta 30 000 px; Trazo llega a 16 384) | PSD versión 1. PSB (versión 2) se rechaza con un mensaje claro |
| Color | RGB, 8 bits por canal | RGB y escala de grises, 8 o 16 bits (16 → 8 tomando el byte alto). CMYK, Lab, indexado, mapa de bits y 32 bits se rechazan con un mensaje claro |
| Capas | Cada capa recortada a su contenido, con nombre (ASCII y Unicode `luni`), opacidad, visibilidad, modo, recorte, bloqueo alfa («bloquear píxeles transparentes») y canal alfa | Ídem. Las capas pueden tener cualquier rectángulo, también fuera del lienzo: se colocan y se recortan al lienzo. Photoshop guarda las capas de 16 bits en el bloque `Lr16`: también se leen. Si hay más capas de las que Trazo admite en ese tamaño, se importa la imagen aplanada y se avisa |
| Modos de fusión | Los 16 modos de Trazo tienen equivalente exacto (`norm`, `mul `, `dark`, `idiv`, `scrn`, `lite`, `div `, `over`, `sLit`, `hLit`, `diff`, `smud`, `hue `, `sat `, `colr`, `lum `) | Los de Photoshop sin equivalente se aproximan y se avisa: subexposición y sobreexposición lineal → subexposición y sobreexposición de color; color más oscuro y más claro → oscurecer y aclarar; luz intensa, lineal, focal y mezcla definida → luz fuerte; restar y dividir → diferencia y sobreexposición de color; disolver → normal |
| Máscaras de capa | Canal −2 con el rectángulo del lienzo y color por defecto blanco | Canal −2 con su rectángulo y su color por defecto; se convierte en una máscara del tamaño del lienzo |
| Grupos | Carpeta (`lsct` 1 o 2, abierta o contraída) con su nombre, opacidad, modo y visibilidad, y su separador (`lsct` 3) | Carpetas anidadas: Trazo tiene un nivel, así que las capas de una carpeta interior pasan a la carpeta exterior y se avisa. «Pasar a través» se importa como grupo normal (aislado) y se avisa |
| Fondo | Si el color de fondo está visible, se exporta como la capa inferior «Fondo» | Una capa de fondo se importa como capa normal |
| Imagen compuesta | Siempre (los lectores sin capas la usan): el documento aplanado, sobre blanco si el fondo está oculto | Si el archivo no tiene capas, la imagen compuesta pasa a ser la única capa |
| Compresión | PackBits (RLE) por fila | Sin comprimir y RLE. ZIP se rechaza con un mensaje claro |
| Sin píxeles | — | Las capas de ajuste y de relleno se omiten y se cuentan en el aviso. Los efectos de capa (sombras, trazos) no se importan |
| Tamaño | Si alguna sección supera los 4 GB que admite PSD, se avisa | Lienzo fuera de los límites de Trazo (16 a 16 384 px por lado, 64 Mpx) → se rechaza con mensaje; no se reescala un documento con capas |
| Abrir | Acciones → Compartir → «PSD (Photoshop)…» | «Importar» en la galería acepta `.psd`, y también «Abrir con → Trazo» |

- **Alfa:** PSD guarda los canales sin premultiplicar y la GPU los guarda premultiplicados. La conversión se hace en un solo sitio, en ambos sentidos, con redondeo.
- **Seguridad:** el lector comprueba cada longitud contra lo que queda del archivo antes de reservar memoria (un plano con RLE ocupa como mínimo 2 bytes por cada 128 píxeles de fila, así que nunca se reserva más de unas 64 veces lo que mide el archivo), y un archivo truncado o malformado lanza `InvalidDataException`, nunca otra cosa (SECURITY.md).
- **Verificación cruzada:** `tools/psd-interop` usa ag-psd 31.0.3 (la biblioteca de Photopea), fijada con `package-lock.json` y elegida con más de dos semanas publicada. Dependabot propone versiones nuevas con 14 días de espera.

## 3. Criterios de aceptación

| ID | Criterio | Prueba |
|---|---|---|
| EX-01.1 | PackBits comprime y descomprime exactamente cualquier fila, incluidas las de 1 byte y las de 128 o más repeticiones | `PackBitsRoundTrips` |
| EX-01.2 | Escribir y leer devuelve las mismas capas: nombres Unicode, rectángulos, opacidad, visibilidad, modo, recorte, píxeles, máscara y grupos; las carpetas anidadas se juntan en la exterior con aviso | `PsdRoundTripPreservesEverything`, `NestedFoldersFlattenToOuterGroupWithWarning` |
| EX-01.3 | Los 16 modos tienen clave propia; las claves sin equivalente se aproximan y se avisa | `BlendKeysMapBothWays` |
| EX-01.4 | Capas con rectángulos parciales o fuera del lienzo se colocan y se recortan bien | `LayerRectsArePlacedAndClipped` |
| EX-01.5 | Escala de grises, 16 bits y archivos sin capas se importan; PSB, CMYK y ZIP se rechazan con mensaje | `ImportsGrayscale16BitAndFlat`, `RejectsUnsupportedFlavors` |
| EX-01.6 | Ningún archivo truncado o con bytes cambiados provoca otra cosa que `InvalidDataException` (más de 20 000 variantes por ejecución) | `MalformedFilesFailCleanly`, `WriteRejectsMismatchedBuffers` |
| EX-01.6b | El paso de premultiplicado a no premultiplicado y vuelta es exacto para todo píxel válido de la GPU | `AlphaConversionRoundTripsGpuPixels` |
| EX-01.7 | Con el motor real: exportar y volver a importar da los mismos píxeles de cada capa y máscara, la misma imagen compuesta (± 1 nivel) y la misma estructura | `PsdExportImportMatchesDocument`, `CompositeIsTheFlattenedDocument`, `FlatAndOversizedPsdImports` (WARP) y la autoprueba en GPU real |
| EX-01.8 | Una implementación independiente (ag-psd) lee lo que exporta Trazo con los mismos nombres, opacidades, modos, máscaras, grupos y píxeles, y Trazo lee lo que escribe ag-psd | `AgPsdReadsTrazoExport`, `TrazoReadsAgPsdOutput` (en CI con `TRAZO_REQUIRE_INTEROP=1`) |

## 4. Fuera de esta iteración

Texto editable y objetos inteligentes como tales (se importan sus píxeles), capas de ajuste, efectos de capa, PSB, CMYK, perfiles de color ICC (se trata todo como sRGB) y abrir archivos `.procreate`.
