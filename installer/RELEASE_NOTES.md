## Trazo 1.3.4 — estudio de pintura para Windows · *painting studio for Windows*

Trazo: pintura con lápiz y dedos en la GPU, para tabletas, portátiles 2 en 1 y pantallas con lápiz. Tus obras se guardan en `Documentos\Trazo`, con copia de seguridad `.bak` en cada guardado.

*Trazo is a GPU painting app for pen and touch on Windows tablets, 2-in-1 laptops and pen displays. **Now in English** — see below.*

### Descarga · *Download*

| Archivo · *File* | Para · *For* |
|---|---|
| `Trazo-1.3.4-x64-setup.exe` | La mayoría de los PC (Intel/AMD) · *Most PCs (Intel/AMD)* |
| `Trazo-1.3.4-arm64-setup.exe` | Equipos ARM (Surface Pro X / Snapdragon) · *ARM devices* |
| `…-portable.zip` | Usar sin instalar · *No install: unzip and open `Trazo.exe`* |

Requiere Windows 10 versión 2004 o posterior. Instala solo para tu usuario y no pide permisos de administrador. *Requires Windows 10 version 2004 or later; installs per user, no admin rights.*

### Aviso de Windows SmartScreen · *SmartScreen warning*

El instalador **todavía no tiene firma digital**, así que Windows puede mostrar "Windows protegió su PC". Para instalar: **Más información → Ejecutar de todas formas**. Puedes comprobar la integridad del archivo con `SHA256-*.txt`.

*The installer is **not yet code-signed**, so Windows may show "Windows protected your PC". Choose **More info → Run anyway**. You can check the file with `SHA256-*.txt`.*

### Novedades de la 1.3.4

- **Corrección importante:** Trazo se cerraba solo si movías el lápiz o el ratón justo cuando QuickShape mostraba «Línea creada» (u otra forma). Ya no pasa.
- **Menos pixelado:** una imagen pequeña se importa agrandada (×2 a ×4) para que lo que dibujes encima se vea nítido. Para una obra que ya tienes: **Acciones › Lienzo › Duplicar la resolución**. Además, el lienzo solo se ve píxel a píxel cuando te acercas mucho.
- **Mano** para moverte por el lienzo: botón junto al pincel o tecla **H**. El lápiz, el ratón o un dedo arrastran el lienzo sin dibujar.

### What's new in 1.3.4

- **Important fix:** Trazo closed by itself if you moved the pen or mouse right when QuickShape showed "Line created" (or another shape). This no longer happens.
- **Less pixelation:** small images are imported enlarged (×2 to ×4) so what you draw on top looks sharp. For an existing artwork: **Actions › Canvas › Double the resolution**. The canvas also only shows individual pixels when you zoom in very close.
- **Hand tool** to move around the canvas: button next to the brush or the **H** key. The pen, mouse or a finger drag the canvas without drawing.

### Novedades de la 1.3.3

- **Corrección:** con Trazo como programa predeterminado, hacer doble clic en una imagen (PNG, JPG, BMP, GIF, TIFF, WebP) abría la galería sin hacer nada. Ahora la imagen se importa como obra nueva. Los archivos `.trazo` y PSD se abren como antes.
- Un tipo de archivo que Trazo no abre muestra un aviso, y una obra ya abierta en otra ventana de Trazo no se abre dos veces.
- Los avisos y errores quedan 8 segundos en pantalla.
- Trazo aparece en «Abrir con» para PSD e imágenes.

### What's new in 1.3.3

- **Fix:** with Trazo as the default app, double-clicking an image (PNG, JPG, BMP, GIF, TIFF, WebP) opened the gallery and did nothing. The image is now imported as a new artwork. `.trazo` and PSD files open as before.
- Unsupported file types show a message, and an artwork already open in another Trazo window isn't opened twice.
- Warnings and errors stay on screen for 8 seconds.
- Trazo appears in "Open with" for PSD files and images.

### Novedades de la 1.3.2

- **Corrección importante:** si instalabas Trazo «para todos los usuarios» y lo abrías desde la última pantalla del instalador, se ejecutaba como administrador y Windows no dejaba **importar, abrir ni guardar archivos** (sin ningún aviso). El instalador ya no lo abre así, Trazo te avisa si se está ejecutando como administrador, y todas las ventanas de archivos avisan si fallan.
- Si te pasó con la 1.3.1: cierra Trazo y ábrelo desde el menú Inicio.

### What's new in 1.3.2

- **Important fix:** when installed "for all users" and launched from the installer's last screen, Trazo ran as administrator and Windows blocked **importing, opening and saving files** without any message. The installer no longer launches it that way, Trazo warns if it is running as administrator, and every file dialog reports failures. If this happened to you with 1.3.1, close Trazo and open it from the Start menu.

### Novedades de la 1.3.1: más fiable

Revisamos todo el código en busca de errores y corregimos 36, cada uno con su prueba. Los que más se notan:

- **No pierdes trabajo al cerrar:** cerrar la obra o la ventana justo mientras se autoguardaba podía perder lo último. Ahora se espera al guardado y, si falla, Trazo pregunta antes de salir.
- **Archivos dañados o bloqueados:** si una obra está dañada se recupera la copia de seguridad; si otro programa la tiene abierta (antivirus, OneDrive), Trazo avisa en lugar de abrir la copia vieja.
- **Trazos:** después de usar muchos colores, un pincel podía fallar al empezar el trazo. **Difuminar** ahora se ve mientras arrastras.
- **Capas:** *Combinar hacia abajo* y *Combinar grupo* ya no cambian la imagen; las capas eliminadas liberan la memoria de la tarjeta gráfica.
- **Herramientas:** abrir una herramienta cierra la anterior (ninguna se queda activa sin su panel) y los atajos no actúan por debajo de Transformar, un ajuste o Licuar. Cambiar de herramienta aplica el ajuste abierto.
- **Panel de color:** *Nueva* y *Renombrar* muestran el cambio al momento; *Eliminar paleta* pide confirmación.
- **PSD:** un archivo malicioso ya no puede hacer que Trazo reserve gigas de memoria.

### What's new in 1.3.1: more reliable

We reviewed the whole codebase and fixed 36 bugs, each with its own test: no more lost work when closing during an autosave, damaged files recover from the backup, a locked file is no longer replaced by the old backup, brushes no longer fail after many colors, smudge shows while dragging, *Merge down* and *Merge group* keep the image unchanged, deleted layers free GPU memory, and tools no longer stay half-open.

### Novedades de la 1.3

- **Panel de color nuevo:** pestañas de disco, **armonía** (complementario, dividido, análogo, triádico y tetrádico), valor (HSB, RGB y hexadecimal) y **paletas** con nombre. Importa y exporta paletas de **Procreate (.swatches)** y de **Adobe (.ase)**. Tu paleta anterior se conserva como «Mi paleta».
- **Time-lapse:** cada obra se graba sola mientras pintas. Míralo en *Acciones › Lienzo › Time-lapse* y expórtalo como vídeo MP4 completo o de 30 segundos. Se puede desactivar en Preferencias.
- **Ajustes nuevos:** curvas por canal, mapa de degradado, equilibrio de color, desenfoque de movimiento y resplandor.
- **Capa de referencia:** marca la capa de línea como *Referencia* y el relleno y la selección automática la usan como límite, aunque esté oculta.
- **Correcciones:** la intensidad intermedia de «Invertir colores» y «Escala de grises» funcionaba al revés; la etiqueta «recorte» no se traducía al inglés.

### What's new in 1.3

- **New color panel** with Disc, **Harmony**, Value and **Palettes** tabs. Import and export **Procreate (.swatches)** and **Adobe (.ase)** palettes.
- **Time-lapse recording** of every artwork, with a player and MP4 export (full or 30 seconds).
- **New adjustments:** Curves, Gradient Map, Color Balance, Motion Blur and Bloom.
- **Reference layer** for fill and automatic selection.

### Qué incluye · *What's included*

- **Pinceles:** 21 pinceles con presión e inclinación, StreamLine, grano de papel, afinado de extremos, curvas de presión y mezcla húmeda.
- **Herramientas:** Pintar, Difuminar y Borrar; QuickShape; simetría; relleno arrastrando el color; cuentagotas.
- **Capas:** 16 modos de fusión, opacidad, bloqueo alfa, máscara de recorte, máscaras de capa y grupos.
- **Selección, Transformar, Licuar, ajustes de color y guías de dibujo** con Asistente de dibujo.
- **Archivos:** formato `.trazo` abierto con guardado verificado y copia de seguridad; PSD; exportación a PNG, PNG transparente, JPEG y capas sueltas.
- **Recuperación:** aviso tras un cierre inesperado, recuperación si se reinicia la tarjeta gráfica y copia de emergencia.

### Limitaciones conocidas · *Known limitations*

- Todavía sin firma digital (SmartScreen avisa). · *Not code-signed yet.*
- El PSD se verificó con otra implementación (ag-psd, la de Photopea), pero aún no en Photoshop, Procreate ni Krita: si lo pruebas, cuéntanos. · *PSD was verified against ag-psd (Photopea's library) but not yet in Photoshop, Procreate or Krita — feedback welcome.*
- La traducción al inglés aún no la ha revisado un hablante nativo. · *The English translation hasn't been reviewed by a native speaker yet; corrections welcome.*
- No se ha probado con todos los modelos de lápiz ni en equipos ARM. · *Not tested with every pen model or on ARM devices.*
- Sin grupos anidados, selecciones guardadas ni animación. · *No nested groups, saved selections or animation.*

Reporta errores en · *Report issues at* [Issues](https://github.com/M4ur0-Gatt1/trazo/issues).
