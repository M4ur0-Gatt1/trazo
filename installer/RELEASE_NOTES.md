## Trazo 1.1 — estudio de pintura para Windows

Trazo: pintura con lápiz y dedos en la GPU, para tabletas, portátiles 2 en 1 y pantallas con lápiz. Tus obras se guardan en `Documentos\Trazo`, con copia de seguridad `.bak` en cada guardado.

### Descarga

| Archivo | Para |
|---|---|
| `Trazo-1.1.0-x64-setup.exe` | La mayoría de los PC (Intel/AMD) |
| `Trazo-1.1.0-arm64-setup.exe` | Equipos ARM (Surface Pro X / Snapdragon) |
| `…-portable.zip` | Usar sin instalar: descomprimir y abrir `Trazo.exe` |

Requiere Windows 10 versión 2004 o posterior. Instala solo para tu usuario y no pide permisos de administrador.

### Aviso de Windows SmartScreen

El instalador **todavía no tiene firma digital**, así que Windows puede mostrar "Windows protegió su PC". Para instalar: **Más información → Ejecutar de todas formas**. Puedes comprobar la integridad del archivo con `SHA256-*.txt`.

### Qué incluye

- **Pinceles:** 21 pinceles con presión e inclinación, StreamLine, grano de papel (en movimiento o texturizado), afinado de extremos, **curvas de presión** para el tamaño, el flujo y la opacidad, y **mezcla húmeda** (dilución, carga, ataque y arrastre del color del lienzo) en el gouache, el óleo y la acuarela.
- **Herramientas:** Pintar, Difuminar y Borrar; QuickShape; simetría; relleno arrastrando el color; cuentagotas.
- **Capas:** 16 modos de fusión, opacidad, bloqueo alfa, máscara de recorte, **máscaras de capa** y **grupos**.
- **Selección:** automática, a mano, rectángulo y elipse; añadir, restar, intersecar, invertir, difuminar, copiar y pegar.
- **Transformar:** libre, uniforme, **distorsionar** (perspectiva) y **deformar** (malla), con modo magnético.
- **Ajustes:** tono, saturación y brillo, contraste, exposición, temperatura, desenfoque, enfoque, ruido y más, y **Licuar**.
- **Guías de dibujo:** cuadrícula 2D, isométrica y perspectiva con **Asistente de dibujo**.
- **Archivos:** formato `.trazo` abierto con guardado verificado y copia de seguridad; exportación a PNG, PNG transparente, JPEG y capas sueltas.

**Novedades de la 1.1**

- **Corrección importante:** en la versión 1.0 instalada con el `.exe`, **«Añadir texto» fallaba** y la etiqueta de QuickShape no se mostraba. Ya funcionan.
- **No pierdas tu obra:** si Trazo se cierra de forma inesperada, al volver a abrirlo te avisa de qué se conservó y te ofrece abrirlo. Si la tarjeta gráfica se reinicia (por ejemplo, al actualizar su controlador), la obra se recupera sola desde el último guardado. Ante un error inesperado se guarda una copia «(recuperado)» sin tocar tu archivo.
- **Más rápido con lienzos grandes:** el autoguardado solo guarda las capas que cambiaron (con 20 capas de 4K, de 9 s a medio segundo), la vista no vuelve a componer las capas que no cambian, y Deformar va mucho más fluido.
- **Acerca de Trazo** (Acciones): versión, licencias y avisos de terceros, y acceso a los registros para informar de un problema.

**Novedades de la 1.0**

- **Curvas de presión:** en Ajustes del pincel, una curva editable para el tamaño, otra para el flujo y otra para la opacidad.
- **Opacidad separada del flujo:** con *Opacidad por presión*, apretar menos limita lo oscuro que llega a ser el trazo aunque pases varias veces; el flujo, en cambio, se acumula.
- **Mezcla húmeda:** los pinceles de pintura arrastran el color que ya hay en la capa y la pintura se va acabando a lo largo del trazo. El gouache, el óleo y la acuarela la traen activada; puedes ajustarla o desactivarla en Ajustes del pincel.

### Limitaciones conocidas

- Todavía sin firma digital (SmartScreen avisa).
- No se ha probado con todos los modelos de lápiz ni en equipos ARM: cuéntanos cómo funciona el tuyo.
- La mezcla húmeda usa el color de la capa activa, no el de las demás capas.
- Sin selecciones guardadas, grupos anidados, animación ni importación de PSD.

Reporta errores en [Issues](https://github.com/M4ur0-Gatt1/trazo/issues).
