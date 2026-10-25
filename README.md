# Trazo

**Estudio de pintura digital nativo para Windows**, pensado para tabletas, portátiles 2 en 1 y pantallas con lápiz (Surface Pen, Wacom, Windows Ink). Se inspira en el flujo de trabajo de Procreate: pinceles con presión, capas, gestos multitáctiles y una interfaz que se usa con el dedo y el lápiz.

Está hecho con **C# / .NET 10**, **WinUI 3** (Windows App SDK 1.8) y **Win2D**: las capas, los pinceles, los modos de fusión y los ajustes se procesan en la **tarjeta gráfica** mediante Direct2D.

> Trazo es un proyecto independiente y no está afiliado a Savage Interactive ni a Procreate®.

## Descargar

**Versión actual: 1.1.0** ([historial](CHANGELOG.md)).

Trazo es **gratis** aquí, con el código abierto. Pronto estará también en la **Microsoft Store**, de pago: es la misma app, con instalación sin avisos de SmartScreen y actualizaciones automáticas, y comprarla financia el desarrollo.

En [Releases](https://github.com/M4ur0-Gatt1/trazo/releases):

- `Trazo-x.y.z-x64-setup.exe`: instalador para PC Intel/AMD. Instala solo para tu usuario, sin pedir permisos de administrador, y asocia los archivos `.trazo`.
- `Trazo-x.y.z-arm64-setup.exe`: instalador para equipos ARM (Surface Pro X, Snapdragon).
- `*-portable.zip`: la app sin instalar.

Requiere **Windows 10 2004 (19041)** o posterior. No hace falta instalar .NET aparte: va incluido.

## Funciones

**Pintura en la GPU**
- 21 pinceles en 6 categorías (Bocetos, Entintado, Pintura, Aerógrafos, Caligrafía, Texturas) con puntas generadas por código.
- Presión e inclinación del lápiz, con curva de presión ajustable y lectura de los puntos intermedios del lápiz para trazos fluidos a alta frecuencia.
- **StreamLine**, espaciado, flujo, dispersión y variación de tamaño, editables por pincel y guardados.
- **Curvas de presión** para el tamaño, el flujo y la opacidad. La opacidad por presión pone un tope al trazo que no sube al pasar varias veces; el flujo sí se acumula.
- **Mezcla húmeda**: dilución, carga, ataque y arrastre del color que ya hay en la capa (activada en el gouache, el óleo y la acuarela).
- Herramientas **Pintar, Difuminar y Borrar**, cada una con su pincel, tamaño y opacidad.
- **QuickShape**: mantén el lápiz quieto al final del trazo y se convierte en línea, círculo, elipse o polilínea, que puedes seguir ajustando.
- **Simetría** vertical, horizontal, en cuadrante o radial (8 ejes).
- **Relleno** arrastrando el círculo de color al lienzo, con umbral ajustable.
- Cuentagotas al tocar y mantener, con el botón lateral del lápiz, con clic derecho, con Alt+clic o con la tecla I.

**Capas**
- 16 modos de fusión nativos de Direct2D, opacidad, bloqueo alfa y máscara de recorte.
- Duplicar, combinar hacia abajo, invertir, rellenar, renombrar y reordenar.
- Hasta 250 capas, según el tamaño del lienzo y un presupuesto de 2 GB de memoria de vídeo.
- Color de fondo, que se puede ocultar para exportar con transparencia.

**Selección y máscaras**
- **Seleccionar**: automática (por color, con umbral), a mano, rectángulo y elipse; nueva, añadir, restar e intersecar; invertir, difuminar, copiar y pegar y seleccionar el contenido de una capa.
- Con una selección activa, pincel, borrador, difuminar, relleno, ajustes y transformar solo cambian la zona seleccionada.
- **Máscaras de capa** en escala de grises: se pintan con cualquier pincel (blanco muestra, negro oculta) y se pueden invertir, aplicar o eliminar.
- **Grupos de capas** con opacidad, modo de fusión y visibilidad propios; contraer, combinar, desagrupar y eliminar.

**Ajustes y transformación**
- Tono/saturación/brillo, contraste, exposición, temperatura y matiz, desenfoque gaussiano, enfocar, ruido, invertir, escala de grises, sepia y opacidad, todos con vista previa en vivo.
- **Transformar** en cuatro modos: Libre y Uniforme (asas de esquina, de lado y de giro), **Distorsionar** (perspectiva por las cuatro esquinas) y **Deformar** (malla que se dobla). Modo Magnético, voltear, rotar 90°, ajustar al lienzo e interpolación suave o nítida.
- **Licuar**: empujar, girar a la derecha o a la izquierda, pellizcar, expandir y reconstruir, con tamaño, presión, Ajustar y Restablecer.

**Guías de dibujo**
- Cuadrícula 2D, isométrica y perspectiva de 1, 2 o 3 puntos de fuga, con tamaño, grosor, opacidad y color. Se guardan con el lienzo.
- **Asistente de dibujo**: los trazos se pegan a las horizontales y verticales, a los ejes isométricos o a las líneas de los puntos de fuga.

**Archivos**
- Galería con autoguardado en `Documentos\Trazo`.
- Formato `.trazo` abierto: un ZIP con `manifest.json`, una PNG por capa (y por máscara) y una miniatura. Abre también los proyectos de la versión web.
- Exportación a PNG, PNG transparente, JPEG, capas sueltas y `.trazo`.
- Importar imágenes, pegar desde el portapapeles y añadir texto.
- Lienzos de hasta 16 384 px por lado (64 megapíxeles) y 150 pasos de deshacer, con un límite de memoria.

## Gestos

| Gesto | Acción |
|---|---|
| Tocar con dos dedos | Deshacer |
| Tocar con tres dedos | Rehacer |
| Tocar con cuatro dedos | Ocultar o mostrar la interfaz |
| Pellizcar y girar con dos dedos | Zoom, desplazar y rotar (se ajusta a 0°/90°) |
| Pellizco rápido | Ajustar a la pantalla |
| Tocar y mantener con el dedo | Cuentagotas |
| Mantener al final del trazo | QuickShape |
| Extremo borrador del lápiz | Borrador temporal |
| Botón lateral del lápiz | Cuentagotas |

**Rechazo de palma:** la primera vez que usas un lápiz se activa el modo *Solo lápiz*: el lápiz dibuja y el dedo navega. Los toques que llegan mientras el lápiz dibuja se ignoran. Se cambia en *Acciones › Preferencias*.

## Atajos de teclado

| Tecla | Acción |
|---|---|
| `B` · `E` · `S` | Pintar · Borrar · Difuminar |
| `[` · `]` | Tamaño del pincel |
| `1`…`9` | Opacidad 10…90 % |
| `I` · `G` · `V` · `X` | Cuentagotas · Relleno · Transformar · Color anterior |
| `M` · `Esc` | Seleccionar · Salir de la selección |
| `Ctrl+A` · `Ctrl+D` · `Ctrl+Mayús+I` | Seleccionar todo · Deseleccionar · Invertir selección |
| `L` · `C` | Capas · Color |
| `Ctrl+Z` · `Ctrl+Y` | Deshacer · Rehacer |
| `Ctrl+J` · `Ctrl+E` | Duplicar capa · Combinar hacia abajo |
| `Espacio` + arrastrar · rueda | Desplazar · Zoom |
| `Alt` + rueda · `R` | Rotar · Quitar rotación |
| `Ctrl+0` · `Ctrl+1` | Ajustar · 100 % |
| `Tab` | Pantalla completa |
| `Ctrl+S` | Guardar |

## Compilar

Requisitos: Windows 10/11 y el [SDK de .NET 10](https://dotnet.microsoft.com/download). No hace falta Visual Studio.

```bash
dotnet test tests/Trazo.Core.Tests
```

```bash
dotnet run --project src/Trazo.App
```

Para la autoprueba de extremo a extremo, que pinta con el motor real en la GPU y escribe un informe y una imagen en la carpeta indicada:

```bash
dotnet run --project src/Trazo.App -- --selftest ./selftest
```

Para generar el instalador (requiere [Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```bash
dotnet publish src/Trazo.App -c Release -r win-x64 -o artifacts/publish/win-x64
```

```bash
ISCC.exe /DArch=x64 installer/Trazo.iss
```

Para publicar una versión, crea una etiqueta `vX.Y.Z`: el flujo `release.yml` compila los instaladores x64 y ARM64 y los adjunta a la release.

> Si clonas el repositorio en una ruta muy larga, Windows puede superar el límite de 260 caracteres al compilar WinUI. Clónalo en una ruta corta, como `C:\src\trazo`.

## Estructura

```
src/Trazo.Core/             Lógica sin GPU ni interfaz: pipeline de trazo (StrokeBuilder),
                            pinceles, QuickShape, simetría, relleno, historial,
                            formato .trazo y guardado transaccional (ProjectSaver), vista
src/Trazo.Rendering/        Motor de pintura en la GPU (Win2D/Direct2D), sin XAML:
                            documento y capas, pinceles, composición, ajustes, transformar
src/Trazo.App/              App WinUI 3: galería, estudio (lienzo, entrada, paneles), servicios
tests/Trazo.Core.Tests      Pruebas unitarias (BR-01, FI-01, historial, formato…)
tests/Trazo.Rendering.Tests Pruebas del motor real con Direct3D WARP e imágenes de referencia (BR-02, LY-01)
docs/                       Base técnica: requisitos, protocolos, decisiones de arquitectura, investigación, resultados
installer/                  Script de Inno Setup
web/                        Versión web (PWA) publicada en GitHub Pages
tools/                      Iconos, capturas y hoja de contacto de referencias
```

## Calidad

El desarrollo sigue una regla: **cada función tiene una especificación, cada especificación tiene pruebas y cada error corregido deja una prueba de regresión.**
La [matriz de requisitos](docs/requirements.md) muestra el estado real de cada módulo con 5 puertas de cierre: investigación, especificación, implementación, pruebas y aprobación.

- **Pruebas:** lógica pura en `Trazo.Core.Tests`, el motor de dibujo con Direct3D WARP e imágenes de referencia en `Trazo.Rendering.Tests`, y una autoprueba de extremo a extremo en la GPU real (`--selftest`).
- **Rendimiento:** `tools/Trazo.Benchmarks` mide trazo, composición, Licuar, Deformar, guardado y apertura en un lienzo de 4K con 20 capas, contra presupuestos de tiempo ([PF-01](docs/specs/PF-01-performance.md)).
- **Recuperación:** autoguardado verificado cada 3 s, aviso al volver a abrir tras un cierre inesperado, recuperación si se reinicia la tarjeta gráfica y copia de emergencia ante un error inesperado ([RC-01](docs/specs/RC-01-recovery.md)). `tools/crash-test.ps1` lo comprueba matando el proceso de verdad.

Para contribuir, mira [CONTRIBUTING.md](CONTRIBUTING.md). Las vulnerabilidades se informan según [SECURITY.md](SECURITY.md). Los componentes de terceros y sus licencias están en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Versión web

La primera versión, una PWA en JavaScript, sigue disponible en <https://m4ur0-gatt1.github.io/trazo/> para probar Trazo desde el navegador.

## Hoja de ruta

- Selecciones guardadas y pluma de selección
- Grupos anidados
- Curvas, mapa de degradado y más desenfoques
- Grabación en time-lapse
- Importar y exportar PSD
- Publicación en la Microsoft Store, de pago (el paquete MSIX ya se compila: ver [docs/store](docs/store/README.md))

## Créditos

- Tipografías [Inter](https://github.com/rsms/inter) (interfaz) y [Space Grotesk](https://github.com/floriankarsten/space-grotesk) (marca y títulos), bajo la SIL Open Font License 1.1. Las licencias se incluyen en `src/Trazo.App/Assets/Fonts/` y `web/fonts/`.
- Iconos generados con `tools/make-icon.ps1`.

## Licencia

El código de Trazo está bajo licencia [MIT](LICENSE). Las tipografías conservan su propia licencia (OFL).
