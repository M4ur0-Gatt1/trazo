## Trazo beta — estudio de pintura para Windows

> **Versión beta.** Puede tener errores. Tus obras se guardan en `Documentos\Trazo`, con copia de seguridad `.bak` en cada guardado.

### Descarga

| Archivo | Para |
|---|---|
| `Trazo-…-x64-setup.exe` | La mayoría de los PC (Intel/AMD) |
| `Trazo-…-arm64-setup.exe` | Equipos ARM (Surface Pro X / Snapdragon) |
| `…-portable.zip` | Usar sin instalar: descomprimir y abrir `Trazo.exe` |

Requiere Windows 10 versión 2004 o posterior. Instala solo para tu usuario y no pide permisos de administrador.

### Aviso de Windows SmartScreen

El instalador **todavía no tiene firma digital**, así que Windows puede mostrar "Windows protegió su PC". Para instalar: **Más información → Ejecutar de todas formas**. Puedes comprobar la integridad del archivo con `SHA256-*.txt`.

### Qué incluye

Lápiz con presión e inclinación, 21 pinceles, capas con 16 modos de fusión, grupos de capas, máscaras de capa y de recorte, bloqueo alfa, selección, gestos táctiles (deshacer con dos dedos, pellizcar para hacer zoom y girar), QuickShape, simetría, relleno, ajustes de imagen, licuar, transformar con perspectiva y deformación, guías de dibujo con asistente y exportación a PNG/JPEG.

**Novedades de la beta 5**

- **Transformar en cuatro modos** (botón de la barra superior o tecla V): *Libre* y *Uniforme* con asas para escalar y girar, *Distorsionar* (arrastra cada esquina para dar perspectiva) y *Deformar* (una malla que dobla la imagen). *Magnético* ajusta el giro a múltiplos de 15° y alinea el movimiento.
- **Licuar** (Ajustes › Licuar): empujar, girar a la derecha o a la izquierda, pellizcar, expandir y reconstruir, con tamaño, presión, *Ajustar* y *Restablecer*. Respeta la selección.
- **Guías de dibujo** (Acciones › Lienzo › Guía de dibujo): cuadrícula 2D, isométrica y perspectiva de 1 a 3 puntos de fuga. Con el **Asistente de dibujo**, los trazos se pegan a la guía. La guía se guarda con el lienzo; la beta 4 abre estos archivos e ignora la guía.

**Novedades de la beta 4**

- **Selección** (botón de la barra superior o tecla M): automática por color, a mano, rectángulo y elipse. Añadir, restar e intersecar; invertir, difuminar, copiar y pegar. Con una selección activa, pinceles, borrador, relleno, ajustes y transformar solo cambian la zona seleccionada. Ctrl+A, Ctrl+D y Ctrl+Mayús+I.
- **Máscaras de capa:** menú de la capa → *Máscara de capa*. Pinta en negro para ocultar y en blanco para mostrar; toca la miniatura de la máscara o la de la capa para elegir dónde pintas.
- **Grupos de capas** con opacidad, modo de fusión y visibilidad propios: menú de la capa → *Agrupar con la de abajo*.
- Formato `.trazo` versión 3: los archivos guardados con la beta 4 **no se abren en las betas anteriores** (la beta 4 abre todos los anteriores).

**Novedades de la beta 3**

- **Corrección importante:** al alejar mucho el zoom (o al restaurar la ventana minimizada) el lienzo podía dejar de redibujarse. Afectaba a las betas 1 y 2. Tus obras se guardaban igualmente.
- Nuevo icono y nueva identidad visual: tipografías Inter y Space Grotesk, y acento cian.

**Novedades de la beta 2**

- **Grano de papel:** los lápices, la tiza, el carboncillo, la acuarela, el gouache y el óleo tienen textura real. Hay dos modos: *Texturizado* (fijo al lienzo) y *En movimiento* (viaja con el trazo).
- **Afinado de extremos:** las plumas y los rotuladores afinan el inicio y el final del trazo, también con el dedo o el ratón.
- Ajustes de grano y afinado en la biblioteca de pinceles (toca de nuevo el pincel activo → *Ajustes del pincel*).
- Guardado más seguro: verifica el archivo antes de reemplazarlo y conserva una copia `.bak`.

### Limitaciones conocidas

- No se ha probado todavía con todos los modelos de lápiz: cuéntanos cómo funciona el tuyo.
- El grano en movimiento solo se aprecia con flujo bajo.
- Todavía no hay selecciones guardadas, pluma de selección ni grupos anidados.
- Licuar aún no tiene los modos Cristales y Borde.

Reporta errores en [Issues](https://github.com/M4ur0-Gatt1/trazo/issues).
