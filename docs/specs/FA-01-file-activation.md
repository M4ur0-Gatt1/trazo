# FA-01 — Abrir desde el Explorador

**Estado:** implementado · **Fecha:** 2026-11-03
**Código:** `src/Trazo.Core/IO/LaunchFile.cs`, `SessionJournal.IsOpenElsewhere`, `MainWindow.OpenFromExplorerAsync`, `App.OnLaunched`, `installer/Trazo.iss` · **Pruebas:** `FA01_*`, autoprueba y prueba con el proceso real

## 1. Objetivo

Hacer doble clic en un archivo con Trazo como programa predeterminado, usar «Abrir con → Trazo» o arrastrar un archivo sobre el icono tiene que abrir ese archivo.

**Antes de FA-01** solo se atendían `.trazo` y `.psd`. Con una imagen (el caso del usuario: un PNG con Trazo como programa predeterminado), Trazo abría la galería y no hacía nada. Tampoco avisaba.

## 2. Decisiones

- **Qué se abre:** el primer argumento que es un archivo existente. Las opciones `--algo` no cuentan, y se prefiere uno que Trazo sepa abrir.
- **Cómo se abre cada tipo:**

  | Tipo | Qué hace Trazo |
  |---|---|
  | `.trazo` | Lo abre (igual que desde la galería) |
  | `.psd` | Lo importa como obra nueva (EX-01) |
  | Imagen: PNG, JPG/JPEG, BMP, GIF, TIF/TIFF, WebP (las mismas que *Importar*) | La importa como obra nueva de una capa, guardada en la galería, con el nombre del archivo |
  | Cualquier otro | Error que dice qué tipos se admiten (antes no pasaba nada) |

- **La misma obra en dos ventanas:**
  - Cada doble clic abre un proceso nuevo.
  - Si otra instancia viva de Trazo ya tiene ese `.trazo` abierto (según su diario de sesión, RC-01), no se abre otra vez y se avisa. Dos procesos guardando el mismo archivo se pisarían el trabajo.
  - Una instancia muerta no cuenta: eso es una sesión abandonada (RC-01).
- **Duración de los avisos:**
  - Los avisos y errores duran 8 s; los mensajes informativos, 2,2 s.
  - Antes todos desaparecían a los 2,2 s, lo que no daba tiempo a leer un error al arrancar.
- **Instalador:**
  - Registra Trazo en «Abrir con» para `.trazo`, PSD y esas imágenes (`Applications\Trazo.exe\SupportedTypes`).
  - No cambia el programa predeterminado de ningún tipo salvo `.trazo`, que ya lo hacía.
- **Fuera de alcance:** reenviar el archivo a una ventana de Trazo ya abierta (instancia única). Cada archivo abre su propia ventana.

## 3. Criterios de aceptación

| # | Criterio | Prueba |
|---|---|---|
| FA-01.1 | Cada extensión se clasifica bien, sin distinguir mayúsculas | `FA01_1_Classify` (10 casos) |
| FA-01.2 | Un PNG recibido desde el Explorador se abre como imagen (regresión del 2026-11-03) | `FA01_2_ImageFromExplorerIsPicked` y prueba con el proceso real |
| FA-01.3 | Las opciones y los archivos que no existen se ignoran | `FA01_3_OptionsAndMissingFilesAreIgnored` |
| FA-01.4 | Se prefiere un archivo admitido; uno no admitido se avisa | `FA01_4_SupportedPreferredUnsupportedStillReported` y captura del aviso |
| FA-01.5 | Se detecta la misma obra abierta en otra instancia viva, sin contar la propia ni las muertas, y sin borrar diarios | `FA01_5_SameDocumentOpenInAnotherLiveInstanceIsDetected` y prueba con dos procesos |
