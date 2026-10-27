## Trazo 1.3 — estudio de pintura para Windows · *painting studio for Windows*

Trazo: pintura con lápiz y dedos en la GPU, para tabletas, portátiles 2 en 1 y pantallas con lápiz. Tus obras se guardan en `Documentos\Trazo`, con copia de seguridad `.bak` en cada guardado.

*Trazo is a GPU painting app for pen and touch on Windows tablets, 2-in-1 laptops and pen displays. **Now in English** — see below.*

### Descarga · *Download*

| Archivo · *File* | Para · *For* |
|---|---|
| `Trazo-1.3.0-x64-setup.exe` | La mayoría de los PC (Intel/AMD) · *Most PCs (Intel/AMD)* |
| `Trazo-1.3.0-arm64-setup.exe` | Equipos ARM (Surface Pro X / Snapdragon) · *ARM devices* |
| `…-portable.zip` | Usar sin instalar · *No install: unzip and open `Trazo.exe`* |

Requiere Windows 10 versión 2004 o posterior. Instala solo para tu usuario y no pide permisos de administrador. *Requires Windows 10 version 2004 or later; installs per user, no admin rights.*

### Aviso de Windows SmartScreen · *SmartScreen warning*

El instalador **todavía no tiene firma digital**, así que Windows puede mostrar "Windows protegió su PC". Para instalar: **Más información → Ejecutar de todas formas**. Puedes comprobar la integridad del archivo con `SHA256-*.txt`.

*The installer is **not yet code-signed**, so Windows may show "Windows protected your PC". Choose **More info → Run anyway**. You can check the file with `SHA256-*.txt`.*

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
