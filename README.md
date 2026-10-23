# Trazo

**Estudio de pintura digital para tabletas y portátiles táctiles con Windows**, inspirado en el flujo de trabajo de Procreate: pinceles sensibles a la presión, capas, gestos multitáctiles y una interfaz pensada para el dedo y el lápiz.

Es una PWA sin dependencias ni compilación: HTML, CSS y JavaScript con Canvas 2D. Se instala desde Microsoft Edge como una app más de Windows y funciona sin conexión.

> Trazo es un proyecto independiente. No está afiliado a Savage Interactive ni a Procreate®.

## Funciones

**Pintura**
- 21 pinceles en 6 categorías: Bocetos, Entintado, Pintura, Aerógrafos, Caligrafía y Texturas. Las puntas se generan por código: redonda, suave, aerógrafo, grafito, tiza, plana, spray y cerdas.
- Presión e inclinación del lápiz (Windows Ink, Surface Pen, Wacom, etc.), con curva de presión ajustable.
- **StreamLine** (suavizado tipo "cuerda"), espaciado, flujo, dispersión y variación de tamaño, editables por pincel.
- Herramientas **Pintar, Difuminar y Borrar**, cada una con su pincel, tamaño y opacidad.
- **QuickShape**: mantén el lápiz quieto al final del trazo y se convierte en línea, círculo, elipse o polilínea, que puedes seguir ajustando.
- **Guías de simetría**: vertical, horizontal, cuadrante y radial (8 ejes).
- **ColorDrop**: arrastra el círculo de color al lienzo para rellenar, con umbral ajustable.
- Cuentagotas: tocar y mantener, botón lateral del lápiz, Alt+clic o la tecla I.

**Capas**
- Hasta 200 capas (según el tamaño del lienzo), con 16 modos de fusión y opacidad.
- Bloqueo alfa, máscara de recorte, duplicar, combinar hacia abajo, invertir, rellenar, renombrar y reordenar arrastrando.
- Color de fondo, que también puede ocultarse para exportar con transparencia.

**Ajustes y transformación**
- Tono/saturación/brillo, contraste, desenfoque gaussiano, enfocar, ruido, invertir, escala de grises, sepia y opacidad, todos con vista previa en vivo.
- Transformar capa: mover, escalar y girar con los dedos, voltear, rotar 90°, ajustar al lienzo e interpolación suave o nítida.

**Lienzo y archivos**
- Galería con autoguardado en el dispositivo (IndexedDB).
- Presets de lienzo (pantalla, 4K, A4/Carta a 300 ppp, formatos de redes) o tamaño personalizado, hasta 8192 px por lado.
- Exportación a PNG, PNG transparente, JPEG, capas sueltas en PNG y proyecto `.trazo` con todas las capas.
- Importación de imágenes y archivos `.trazo`; pegar desde el portapapeles; texto.
- Historial de deshacer de hasta 120 pasos.

## Gestos

| Gesto | Acción |
|---|---|
| Tocar con dos dedos | Deshacer |
| Tocar con tres dedos | Rehacer |
| Tocar con cuatro dedos | Ocultar o mostrar la interfaz |
| Pellizcar y girar con dos dedos | Zoom, desplazar y rotar (se ajusta a 0°/90°) |
| Pellizco rápido | Ajustar el lienzo a la pantalla |
| Tocar y mantener con el dedo | Cuentagotas |
| Mantener al final del trazo | QuickShape |
| Extremo borrador del lápiz | Borrador temporal |
| Botón lateral del lápiz | Cuentagotas |

**Rechazo de palma:** la primera vez que usas un lápiz, Trazo activa el modo *Solo lápiz*: el lápiz dibuja y el dedo navega. Los toques que llegan mientras el lápiz está dibujando se ignoran. Puedes cambiarlo en *Acciones › Preferencias*.

## Atajos de teclado

| Tecla | Acción |
|---|---|
| `B` / `E` / `S` | Pintar / Borrar / Difuminar |
| `[` / `]` | Achicar / agrandar el pincel |
| `1`…`9` | Opacidad 10…90 % |
| `I` · `G` · `V` · `X` | Cuentagotas · Relleno · Transformar · Color anterior |
| `L` · `C` | Capas · Color |
| `Ctrl+Z` · `Ctrl+Y` / `Ctrl+Shift+Z` | Deshacer · Rehacer |
| `Ctrl+J` · `Ctrl+E` | Duplicar capa · Combinar hacia abajo |
| `Espacio` + arrastrar · rueda | Desplazar · Zoom |
| `Alt` + rueda · `R` | Rotar · Quitar rotación |
| `Ctrl+0` · `Ctrl+1` | Ajustar a la pantalla · 100 % |
| `Tab` | Pantalla completa |
| `Ctrl+S` | Guardar ahora |

## Usarlo

### Instalarlo como app (recomendado)
1. Abre la URL de GitHub Pages del proyecto en **Microsoft Edge** o Chrome.
2. Menú `…` › **Aplicaciones › Instalar Trazo**.
3. Se abre en su propia ventana, aparece en el menú Inicio y funciona sin conexión.

### Ejecutarlo en local
Requiere [Node.js](https://nodejs.org) 18 o superior; no hay dependencias que instalar.

```bash
npm start
```

Después abre <http://localhost:5173>.

### Publicarlo en la Microsoft Store (opcional)
Con [PWABuilder](https://www.pwabuilder.com) se genera un paquete MSIX a partir de la URL publicada.

## Estructura

```
index.html            interfaz
css/app.css           estilos
js/core.js            utilidades, eventos, color, ajustes, iconos
js/storage.js         IndexedDB (galería)
js/brushes.js         puntas generadas y biblioteca de pinceles
js/document.js        documento, capas y composición (fusión, recorte)
js/history.js         deshacer/rehacer con parches de píxeles
js/layers.js          operaciones de capa
js/view.js            vista: zoom, rotación, renderizado
js/engine.js          motor de trazo, difuminar, QuickShape, simetría, relleno
js/adjust.js          ajustes de imagen y transformar
js/input.js           lápiz, gestos, ratón y teclado
js/ui.js, panels.js   barra superior, barra lateral y paneles
js/app.js             galería, guardado y arranque
sw.js                 funcionamiento sin conexión
tools/                servidor local y generador de iconos
```

## Hoja de ruta

- Selección (lazo, rectángulo, varita) y máscaras de capa
- Grupos de capas y modos de fusión dentro de las máscaras de recorte
- Licuar, curvas, mapa de degradado
- Grabación en time-lapse
- Importar y exportar PSD
- Motor de pintura acelerado por WebGL para lienzos muy grandes
- Empaquetado MSIX para la Microsoft Store

## Licencia

[MIT](LICENSE)
