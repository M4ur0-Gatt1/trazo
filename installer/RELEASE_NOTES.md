## Trazo 1.2 — estudio de pintura para Windows · *painting studio for Windows*

Trazo: pintura con lápiz y dedos en la GPU, para tabletas, portátiles 2 en 1 y pantallas con lápiz. Tus obras se guardan en `Documentos\Trazo`, con copia de seguridad `.bak` en cada guardado.

*Trazo is a GPU painting app for pen and touch on Windows tablets, 2-in-1 laptops and pen displays. **Now in English** — see below.*

### Descarga · *Download*

| Archivo · *File* | Para · *For* |
|---|---|
| `Trazo-1.2.0-x64-setup.exe` | La mayoría de los PC (Intel/AMD) · *Most PCs (Intel/AMD)* |
| `Trazo-1.2.0-arm64-setup.exe` | Equipos ARM (Surface Pro X / Snapdragon) · *ARM devices* |
| `…-portable.zip` | Usar sin instalar · *No install: unzip and open `Trazo.exe`* |

Requiere Windows 10 versión 2004 o posterior. Instala solo para tu usuario y no pide permisos de administrador. *Requires Windows 10 version 2004 or later; installs per user, no admin rights.*

### Aviso de Windows SmartScreen · *SmartScreen warning*

El instalador **todavía no tiene firma digital**, así que Windows puede mostrar "Windows protegió su PC". Para instalar: **Más información → Ejecutar de todas formas**. Puedes comprobar la integridad del archivo con `SHA256-*.txt`.

*The installer is **not yet code-signed**, so Windows may show "Windows protected your PC". Choose **More info → Run anyway**. You can check the file with `SHA256-*.txt`.*

### Novedades de la 1.2

- **PSD (Photoshop):** exporta e importa con capas, grupos, máscaras, máscaras de recorte, bloqueo alfa y los 16 modos de fusión. Para pasar tus obras a Photoshop, Procreate, Krita o Photopea y traerlas de vuelta. Exportar: *Acciones › Compartir › PSD (Photoshop)…*; importar: *Importar* en la galería, o *Abrir con → Trazo*. Lee RGB y escala de grises de 8 y 16 bits y te avisa de lo que no puede conservar igual (modos sin equivalente, grupos anidados, capas de ajuste).
- **Interfaz en inglés:** Trazo sigue el idioma de Windows. Puedes elegirlo en *Acciones › Preferencias › Idioma* (se aplica al volver a abrir).
- En Licuar, «Ajustar» se llama ahora «Cantidad», y en Seleccionar el botón de suavizar el borde se llama «Difuminar borde».

### What's new in 1.2

- **English interface.** Trazo follows your Windows language; you can also choose it in *Actions › Preferences › Language* (applies the next time you open Trazo).
- **PSD (Photoshop) import and export** with layers, groups, layer masks, clipping masks, alpha lock and all 16 blend modes — move your artwork to Photoshop, Procreate, Krita or Photopea and back. Export from *Actions › Share › PSD (Photoshop)…*; import with *Import…* in the gallery or *Open with → Trazo*.

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
