# Inventario técnico del Brush Studio de Procreate (iPad) para el motor de pinceles de Trazo

> Documento de investigación. Trazo es una app independiente y no está afiliada a Savage Interactive (Procreate).
> Todo el texto sobre Procreate está **parafraseado** a partir de la documentación oficial; no se reproducen pasajes.

## 0. Encabezado

| Campo | Valor |
|---|---|
| Producto analizado | Procreate para iPad |
| Versión del manual | **Procreate Handbook 5.4** (selector de versiones del manual: 5.0 a 5.4; la página de ajustes está bajo la versión 5.4) |
| Versión de la app vigente según fuente oficial | **5.4.14**: así lo indica la FAQ oficial de help.procreate.com el día de la consulta (requiere iPadOS 16.3 o posterior). Nota: un resultado de búsqueda con caché de esa misma FAQ mostraba 5.4.11, así que la versión menor cambia a menudo. No encontré notas de versión oficiales para cada versión 5.4.x. |
| Fecha de consulta | 2026-10-24 |
| Método | WebFetch y WebSearch sobre dominios oficiales. **Advertencia de fidelidad:** WebFetch entrega el contenido a través de un resumidor automático, no como texto literal. Las cifras y listas de opciones se pidieron explícitamente y vinieron citadas, pero conviene que una persona revise la página original antes de convertir cualquier fila en requisito normativo. |

### URLs consultadas

| URL | Resultado |
|---|---|
| https://help.procreate.com/procreate/handbook/brushes/brush-studio-settings | OK. Es la fuente principal. Los **14 atributos** están todos en esta única página; el manual no tiene una página separada por atributo. |
| https://help.procreate.com/procreate/handbook/brushes/brush-studio | OK. Cubre la interfaz, la Drawing Pad y los ajustes avanzados por parámetro. |
| https://help.procreate.com/procreate/handbook/brushes/dual-brush | OK. Explica Dual Brush y Combine Mode, pero no enumera los modos. |
| https://help.procreate.com/articles/Ls9oMu-procreate-5-4-update-at-a-glance | OK. Lista los cambios de 5.4 que afectan al Brush Studio. |
| https://help.procreate.com/articles/kQ3U9l-procreate-5-4-new-brush-system | OK. Trata almacenamiento y bibliotecas; no menciona el Brush Studio. |
| https://help.procreate.com/articles/dbgjal-procreate-faq | OK. Fuente de la versión vigente (5.4.14). |
| https://procreate.com/procreate/whats-new | Cargó, pero el contenido extraído no traía número de versión ni lista de novedades utilizable. No se usa como fuente. |

### Cambios de 5.4 en el Brush Studio (según la nota oficial «What's new in Procreate 5.4?»)

- **Stroke Path:** nuevos Spacing Jitter, Jitter Lateral y Jitter Linear.
- **Shape:** nuevos Roundness Vertical Jitter y Roundness Horizontal Jitter.
- **Dynamics:** nuevo Speed → Spacing.
- **Rendering:** nuevo Alpha Threshold.
- **Apple Pencil:** nuevo Pressure Graph por pincel.
- **Preview:** pestaña nueva.
- Se corrigió Dynamic Brush Scaling con Dual Brushes.
- El manual también atribuye a 5.4 el renombrado de pinceles desde la biblioteca, la combinación de pinceles por defecto y la opción «Classic Normal Combine» (que conserva el Normal anterior a 5.4).

### Inconsistencias detectadas en la documentación oficial

1. La página «Brush Studio» da dos recuentos distintos de atributos (14 y once). La página de ajustes dice 14 y contiene 14 secciones. En este inventario se usa **14**.
2. «Uniformed Glaze» es probablemente una errata de «Uniform Glaze».
3. La terminología de color es inconsistente: Lightness y Darkness en los jitters, Brightness en Color Pressure y Lightness en Tilt y en Barrel roll.
4. «Use Stamp Preview» aparece tanto en Properties como en Preview.
5. La sección Roughness Source menciona «Metallic Source», lo que parece copiado de la sección Metallic.
6. Hay enlaces internos de la página 5.4 que apuntan a páginas de 5.3.
7. El manual no da rangos numéricos para la mayoría de los deslizadores (Spacing, Flow, Wet Mix, Stabilization, etc.); solo dice que son porcentajes o deslizadores.

### Leyenda

- **Estado en Trazo:** Implementado / Parcial (con lo que falta) / No implementado. Se evaluó sobre `src/Trazo.Core/Brushes/BrushPreset.cs`, `TipGenerator.cs`, `src/Trazo.Core/Strokes/Stamping.cs` y `src/Trazo.App/Painting/BrushEngine.cs`. Como contexto se consultaron también `BrushLibrary.cs`, `StudioView.Panels.cs`, `StudioView.Input.cs` y `StudioView.xaml.cs`, que no se modificaron.
- **«no documentado»:** el manual no da rango, fórmula ni opciones.
- **Hardware:** P = presión, I = inclinación (tilt), Az = azimut, BR = barrel roll (Apple Pencil Pro), V = velocidad, Dedo = táctil. Si la celda dice «(doc.)», el manual lo afirma; «(inferido)» es interpretación nuestra.

### Resumen rápido del motor actual de Trazo (para leer la columna de estado)

- Usa **sellos (dabs)** equiespaciados. El paso es `max(0.35 px, tamaño(presión) × Spacing)`, y `Spacing` es una fracción del diámetro (en la UI va de 0.01 a 2).
- Tamaño por presión lineal: `size × (MinSizePressure + (1−MinSizePressure)·p)`.
- Alfa por sello: `Flow × (1 − FlowPressure + FlowPressure·p)`. Si el sello mide menos de 1.5 px, el alfa se atenúa en proporción.
- La curva de presión es **global** (gamma, en las preferencias del lápiz), no por pincel.
- Tilt: magnitud `√(XTilt²+YTilt²)/70`, limitada a 1, que **se mide desde la vertical**. Solo afecta al tamaño: `size × (1 + Tilt·t²)`. No se calcula el azimut ni se lee Twist (barrel roll).
- Variación aleatoria: `Jitter` (tamaño ±), `Scatter` (desplazamiento isótropo radial de hasta `Scatter·size·2`), `RotationRandom` (giro ± por sello), `RotationFollowsStroke` (booleano) y `Angle` (giro base).
- StreamLine tipo «cuerda»: radio = `StreamLine·26/zoom`. La presión **no** se suaviza.
- Las puntas son 8 máscaras procedurales de 256² (`TipShape`), sin importación de imágenes. La textura de Pencil y Chalk está **horneada en la punta** (gira y escala con el sello): no hay un grano independiente.
- Render: los sellos se acumulan con SourceOver en un búfer por trazo, y al terminar el búfer se compone sobre la capa con la opacidad de la herramienta. Composiciones posibles: SourceOver, SourceAtop (alpha lock) y DestinationOut (borrador). No hay modos de fusión por pincel.
- Difuminar (Smudge): un parche que se arrastra y se re-muestrea. La intensidad está fijada en el código.
- Simetría y QuickShape existen, pero no forman parte del Brush Studio y quedan fuera de este inventario.

---

## 1. Stroke Path (Trayectoria del trazo): 5 subparámetros

| Parámetro (EN) | Traducción | Qué hace (doc.) | Rango/opciones | Interacciones | Hardware | Prueba propuesta | Estado en Trazo |
|---|---|---|---|---|---|---|---|
| Spacing | Espaciado | Frecuencia con la que se estampa la forma a lo largo del trazo. Con valores altos se ven sellos separados; en el mínimo se funden en una línea continua. | Porcentaje; extremos no documentados (el mínimo da un trazo continuo) | Se combina con Spacing Jitter y con Dynamics → Speed → Spacing. El manual dice que existe un espaciado mínimo interno. | Ninguno directo (doc.) | Trazo recto de 1000 px a presión constante: contar los sellos y comprobar que la distancia media entre centros es `f(Spacing)·diámetro`. Con Spacing = 0, el perfil de alfa a lo largo del eje no debe tener ondulación > 1/255. | **Parcial.** Hay espaciado relativo al tamaño con mínimo de 0.35 px. Falta la escala en % y la equivalencia «0 = continuo». Hay que medir la función de mapeo. |
| Spacing Jitter | Variación del espaciado | Aleatoriza el espaciado. Por el mínimo interno, tiene efecto incluso con Spacing al 0 %. | Porcentaje; extremos no documentados | Se suma a Spacing. Puede vincularse a presión. | P (doc.) | Con semilla fija, el histograma de distancias entre sellos debe tener una varianza que crezca de forma monótona con el jitter, y con jitter 0 debe ser ≈ 0. | **No implementado** |
| Jitter Lateral | Variación lateral | Desplaza los sellos en perpendicular a la dirección del trazo. | Porcentaje; extremos no documentados | Puede controlarse con presión, inclinación o barrel roll. | P, I, BR (doc.) | En un trazo horizontal, la desviación en Y de los centros debe ser > 0 y la de X a lo largo de la tangente debe ser ≈ 0 respecto a la ideal. | **Parcial.** `Scatter` desplaza en una dirección aleatoria (isótropa), no en perpendicular, y no se puede vincular a presión ni inclinación. |
| Jitter Linear | Variación longitudinal | Desplaza los sellos a lo largo de la dirección del trazo. | Porcentaje; extremos no documentados | Igual que Jitter Lateral | P, I, BR (doc.) | En un trazo horizontal, la desviación debe estar solo en X y debe conservarse el orden medio de los sellos. | **Parcial.** Solo existe junto con el lateral, mezclado en `Scatter`. Hay que separar los dos ejes. |
| Fall Off | Desvanecimiento | Desvanece el trazo a medida que avanza, empezando con opacidad plena. Con valor alto se vuelve invisible enseguida; en cero no hay desvanecimiento. | no documentado (el extremo «apagado» es sin efecto) | Probablemente multiplica la opacidad o el flujo (inferido) | Ninguno (doc.) | El alfa de los sellos en función de la longitud recorrida debe ser monótona decreciente, y la longitud a la que alfa < 1/255 debe disminuir al aumentar Fall Off. | **No implementado** |

## 2. Stabilization (Estabilización): 6 subparámetros

| Parámetro (EN) | Traducción | Qué hace (doc.) | Rango/opciones | Interacciones | Hardware | Prueba propuesta | Estado en Trazo |
|---|---|---|---|---|---|---|---|
| Stabilization → Amount | Estabilización (cantidad) | Promedio móvil que elimina temblores. **Depende de la velocidad**: cuanto más rápido es el trazo, más se suaviza. | Porcentaje; extremos no documentados | Puede combinarse con StreamLine y Motion Filtering. Hay que observar cómo se componen. | V (doc.) | La misma polilínea con ruido se reproduce a dos velocidades: la desviación respecto a la recta ideal debe ser menor en la reproducción rápida. | **No implementado** |
| StreamLine → Amount | StreamLine (cantidad) | Suaviza los trazos y los vuelve más regulares; con valor bajo o apagado, el trazo es más natural. | Porcentaje; extremos no documentados | No se describe | Ninguno (doc.); el manual no menciona la velocidad | Zig-zag de amplitud A: la amplitud de salida debe ser monótona decreciente con Amount. | **Parcial.** Hay un suavizado «cuerda» con radio dependiente del zoom (`StreamLine·26/zoom`). El algoritmo de Procreate no está documentado, así que la equivalencia queda pendiente de observación. |
| StreamLine → Pressure | StreamLine (presión) | Suaviza la respuesta a la presión: con valor alto la rampa es más larga y suave; con valor bajo, la presión se aplica antes. | Porcentaje; extremos no documentados | Afecta a todo lo que depende de la presión | P (doc.) | Escalón de presión de 0.2 a 1.0: medir el tiempo o la distancia hasta llegar al 90 % del tamaño final, que debe crecer con el parámetro. | **No implementado.** El suavizador copia la presión cruda sin filtrarla. |
| Motion Filtering → Amount | Filtrado de movimiento | Elimina los extremos del temblor sin promediar, con un suavizado uniforme a cualquier velocidad. | Porcentaje; extremos no documentados | Es requisito de Expression | V (el manual dice que es independiente de la velocidad) | Mismo ruido a dos velocidades: la desviación de salida debe ser ≈ igual en ambas. | **No implementado** |
| Motion Filtering → Expression | Expresión | Devuelve parte de la variación expresiva. Solo actúa con Motion Filtering activo, y su efecto se reduce cuanto más alto es Motion Filtering. | Porcentaje; extremos no documentados | Depende de Motion Filtering | Ninguno (doc.) | Con Motion Filtering = 0, Expression no debe tener efecto (salida idéntica bit a bit). | **No implementado** |
| Ámbito global/por pincel | Ámbito | La estabilización puede fijarse por pincel o de forma global (Accesibilidad, en Interface and Gestures). | Global / Por pincel | No se documenta la precedencia global contra pincel | — | Fijar el global y el del pincel con valores distintos y comprobar cuál se aplica (requiere observación en Procreate). | **No implementado.** StreamLine solo existe por pincel. |

## 3. Taper (Afinado de extremos): 13 subparámetros

| Parámetro (EN) | Traducción | Qué hace (doc.) | Rango/opciones | Interacciones | Hardware | Prueba propuesta | Estado en Trazo |
|---|---|---|---|---|---|---|---|
| Pressure Taper → Slider (inicio/fin) | Longitud del afinado | Añade un afinado artificial al principio, al final o en ambos extremos. Al llevar los tiradores hacia el centro, el afinado se acorta. | Dos tiradores; longitud y unidades no documentadas | Link Tip Sizes; se suma al afinado natural de la presión | Lápiz (doc.) | Presión constante 1.0: el tamaño de los sellos en función de la distancia debe subir de ~0 a 100 % en una longitud L₀ y bajar al final en L₁. Medir L en px y en % de la longitud del trazo. | **No implementado** |
| Pressure Taper → Link Tip Sizes | Vincular extremos | Hace que los dos tiradores se muevan juntos. | Interruptor | Slider | — | Mover uno con el vínculo activo y comprobar que el otro cambia igual. | **No implementado** |
| Pressure Taper → Size | Tamaño | Define lo brusca que es la transición de grueso a fino. | no documentado | Opacity, Tip | Lápiz | Ajustar el perfil de diámetro del afinado; la pendiente máxima debe crecer con Size. | **No implementado** |
| Pressure Taper → Opacity | Opacidad | Define lo transparente que queda el afinado en los extremos. | no documentado | Size | Lápiz | Alfa del primer y del último sello contra el valor del parámetro. | **No implementado** |
| Pressure Taper → Pressure | Presión | Usa la presión real para un afinado reactivo que adelgaza más rápido al final. | no documentado | Slider; curva de presión | P (doc.) | Con una caída de presión al final, comparar la longitud del afinado con el parámetro en 0 y en máx. | **No implementado** |
| Pressure Taper → Tip | Punta | Valor bajo imita una punta fina; valor alto, una punta gruesa. | no documentado | Size | Lápiz | Diámetro mínimo en el extremo (en px) contra Tip. | **No implementado** |
| Pressure Taper → Tip Animation | Animación de punta | Activa o desactiva la animación de previsualización del afinado. | Interruptor | Solo UI | — | No aplica al motor (UI). | **No implementado** |
| Touch Taper → Slider | Longitud del afinado táctil | Afinado artificial para el dedo, que no tiene presión. | Dos tiradores; no documentado | Link Tip Sizes | Dedo (doc.) | Trazo táctil: el perfil de diámetro debe tener rampas en los extremos. | **No implementado.** Al tocar con el dedo, la presión es 1 y el trazo sale sin afinar. |
| Touch Taper → Link Tip Sizes | Vincular extremos | Igual que en Pressure Taper | Interruptor | — | Dedo | Igual que en Pressure Taper | **No implementado** |
| Touch Taper → Size | Tamaño | Igual que en Pressure Taper | no documentado | — | Dedo | Igual que en Pressure Taper | **No implementado** |
| Touch Taper → Opacity | Opacidad | Igual que en Pressure Taper | no documentado | — | Dedo | Igual que en Pressure Taper | **No implementado** |
| Touch Taper → Tip | Punta | Igual que en Pressure Taper (no tiene Pressure ni Tip Animation) | no documentado | — | Dedo | Igual que en Pressure Taper | **No implementado** |
| Taper Properties → Classic Taper | Afinado clásico | Recupera el aspecto y el comportamiento del afinado de versiones anteriores. | Interruptor | Sustituye el modelo de afinado | — | Requiere observación (no se documenta en qué consiste la diferencia). | **No implementado** |

**Ambigüedad:** el manual no dice si el afinado se define en longitud absoluta (px o pt) o relativa al trazo, ni cómo se resuelve en trazos más cortos que la suma de los dos afinados. Tampoco explica cómo se entera el motor del «final» del trazo antes de que termine.

## 4. Shape (Forma): 15 subparámetros

| Parámetro (EN) | Traducción | Qué hace (doc.) | Rango/opciones | Interacciones | Hardware | Prueba propuesta | Estado en Trazo |
|---|---|---|---|---|---|---|---|
| Shape Source (Shape Editor) | Fuente de forma | Imagen en escala de grises que hace de punta. Se importa desde Fotos, Archivos, pegando o desde la Source Library (con buscador). | Importación; formato y resolución no documentados | Todo lo de Shape; filtrado | — | Importar un PNG de prueba (cuadrado con una marca de esquina) y comprobar orientación, polaridad (blanco = pintura) y escala. | **Parcial.** Hay 8 puntas procedurales fijas de 256² (`TipShape`). No se pueden importar imágenes ni editar. |
| Shape Behavior → Input Style | Estilo de entrada | Opciones: *Touch only* (el giro sigue Touch Properties); *Azimuth* (la dirección de inclinación del lápiz gira la forma y sustituye a Rotation solo para el lápiz); *Azimuth and barrel roll* (añade el giro del barril; con «Relative to stroke», cada trazo empieza en la misma posición; sin lápiz compatible, vuelve a Azimuth). | Touch only / Azimuth / Azimuth and barrel roll | Sustituye a Rotation; Hover permite previsualizar el giro inicial | Az, BR (doc.) | Con el lápiz inclinado hacia 0°, 90°, 180° y 270°, la rotación del sello debe seguir al azimut (±tolerancia). | **No implementado.** No se calcula el azimut ni se lee Twist. |
| Touch Properties → Rotation | Rotación | Vincula el giro de la forma a la dirección del trazo: 0 % estático, +100 % sigue el trazo, −100 % gira en sentido inverso. | −100 % a +100 % (doc.) | Input Style, Scatter, Roundness graph (ángulo base) | Ninguno | Trazo circular: el ángulo del sello menos el ángulo del trazo debe ser constante (+100 %), el ángulo del sello constante (0 %) o con el signo invertido (−100 %). Medir ±50 % para deducir si la interpolación es lineal. | **Parcial.** `RotationFollowsStroke` es booleano (equivale a 0 o +100 %). Faltan los valores intermedios y los negativos. |
| Shape Properties → Scatter | Dispersión (de giro) | Aleatoriza la rotación de cada sello, sin depender de la dirección del trazo. | Porcentaje; correspondencia en grados no documentada | Se suma a Rotation | Ninguno | Histograma de rotaciones con 10 000 sellos: debe ser uniforme en ±θ(Scatter). Determinar θ(100 %) (¿±180°?). | **Implementado.** `RotationRandom` da un giro uniforme ± por sello. Falta calibrar la escala en % frente a radianes. |
| Shape Properties → Count | Recuento | Estampa varias veces por punto. | Hasta 16 (doc.) | Count Jitter; Scatter; alfa acumulada | Ninguno | Con Count = N y Scatter > 0, el número de sellos debe ser N × el de Count = 1 en el mismo trazo. | **No implementado** |
| Shape Properties → Count Jitter | Variación del recuento | Aleatoriza el recuento entre 1 y Count. Con 0 %, el recuento es fijo. | 0–100 %; al 100 % el rango es [1, Count] (doc.) | Count | Ninguno | Distribución del recuento por punto al 50 % (¿rango [Count/2, Count]? → observación). | **No implementado** |
| Shape Properties → Randomized | Aleatorio | Gira la forma al azar al empezar cada trazo. | Interruptor | Rotation, Scatter | Ninguno | Rotación del primer sello en 100 trazos: debe ser uniforme, y constante dentro de cada trazo si Scatter = 0. | **No implementado** |
| Shape Properties → Flip X | Voltear X | Refleja la forma en horizontal. | Interruptor | Rotation (¿se voltea antes o después de girar? → observación) | Ninguno | Una punta asimétrica debe salir reflejada pixel a pixel. | **No implementado** |
| Shape Properties → Flip Y | Voltear Y | Refleja la forma en vertical. | Interruptor | Igual que Flip X | Ninguno | Igual que Flip X | **No implementado** |
| Brush Roundness Graph | Gráfico de redondez | El nodo verde fija la rotación base; los nodos azules aplastan la forma. | Gráfico; rangos no documentados | Pressure y Tilt Roundness; Rotation | Ninguno | Ángulo base de 45° con redondez 50 %: la caja delimitadora del sello aislado debe medir la mitad en su eje menor. | **Parcial.** `Angle` da la rotación base. Falta la redondez (relación de aspecto): la punta Flat la tiene fija. |
| Shape Properties → Pressure Roundness | Redondez por presión | Aplasta la forma según la presión. | Porcentaje; no documentado | Roundness graph | P (doc.) | Relación de aspecto del sello contra la presión: debe ser monótona. | **No implementado** |
| Shape Properties → Tilt Roundness | Redondez por inclinación | Aplasta la forma según la inclinación. | Porcentaje; no documentado | Roundness graph; umbral de inclinación | I (doc.) | Relación de aspecto contra el ángulo de inclinación. | **No implementado** |
| Roundness Vertical Jitter | Variación vertical de redondez | Comprime la forma en vertical al azar a lo largo del trazo. | Porcentaje; no documentado | Puede vincularse a presión | P (doc.) | Varianza de la altura del sello > 0 con varianza de la anchura = 0. | **No implementado** |
| Roundness Horizontal Jitter | Variación horizontal de redondez | Lo mismo en horizontal. | Porcentaje; no documentado | Puede vincularse a presión | P (doc.) | Al revés que la anterior | **No implementado** |
| Shape Filtering | Filtrado de forma | Antialias de la punta: No Filtering (nítido, puede verse dentado), Classic Filtering (bordes de versiones anteriores) o Improved Filtering (actual). | 3 opciones (doc.) | Grain Filtering | Ninguno | Sellos de 3 a 8 px: con No Filtering solo debe haber alfas de 0/255 en el borde (o casi); comparar el perfil de borde de Classic y de Improved (requiere observación). | **Parcial.** Siempre interpolación lineal sin mipmaps; no se puede elegir. Hay una atenuación ad hoc del alfa por debajo de 1.5 px. |

## 5. Grain (Grano): 16 subparámetros

> **Nota de Trazo:** las puntas Pencil y Chalk incluyen ruido horneado en la máscara. Eso es textura **de forma** (gira y escala con cada sello), no un grano independiente. Por eso toda esta sección figura como no implementada.

| Parámetro (EN) | Traducción | Qué hace (doc.) | Rango/opciones | Interacciones | Hardware | Prueba propuesta | Estado en Trazo |
|---|---|---|---|---|---|---|---|
| Grain Source (Grain Editor) | Fuente de grano | Textura que se importa, edita y repite en mosaico. Incluye una biblioteca de más de 100 texturas. | Importación; formato no documentado | Todo Grain | — | Importar un grano de tablero de ajedrez y verificar que se repite sin costuras. | **No implementado** |
| Grain Behavior → Moving / Texturized | Comportamiento: en movimiento / texturizado | *Moving* arrastra el grano con el trazo (marcas estriadas). *Texturized* lo deja fijo detrás del trazo, como una plantilla. | 2 opciones (doc.) | Varios parámetros solo funcionan con Moving | Ninguno | Texturized: dos trazos solapados en zonas distintas deben mostrar el mismo patrón alineado al lienzo (correlación ≈ 1). Moving: la correlación con el patrón del lienzo debe ser baja. | **No implementado** |
| Movement | Movimiento | Con valor bajo el grano se emborrona; con valor alto «rueda» como en Texturized. | Solo con Moving; rango no documentado | Grain Behavior | Ninguno | Medir la anisotropía (desenfoque direccional) del grano contra Movement. | **No implementado** |
| Scale | Escala | Tamaño del grano dentro de la forma. | no documentado | Zoom | Ninguno | Período del patrón en px contra Scale (determinar si es lineal). | **No implementado** |
| Zoom | Zoom | Va de *Cropped* (tamaño fijo de la textura) a *Follow Size* (escala con el pincel). Follow Size requiere Moving. | Cropped ↔ Follow Size | Scale; Moving | Ninguno | Variar el tamaño del pincel ×2: con Cropped el período no cambia; con Follow Size se duplica. | **No implementado** |
| Rotation | Rotación del grano | Emborrona el grano según la dirección del trazo: −100 % inverso, 0 % fijo, Follow Stroke. Requiere Moving. | −100 % … 0 % … Follow Stroke | Moving | Ninguno | Trazo circular: orientación del patrón contra la tangente. | **No implementado** |
| Depth | Profundidad | Intensidad de la textura sobre el color base: el mínimo la oculta y el máximo la deja oscura y viva. | no documentado | Blend Mode, Depth Minimum, Depth Jitter | Puede ser avanzado (P/I) (inferido) | Contraste RMS del trazo contra Depth: debe ser monótono, ≈ 0 en el mínimo. | **No implementado** |
| Depth Minimum | Profundidad mínima | Suelo de contraste. Requiere Moving y solo actúa con Depth Jitter o con Depth avanzado. | no documentado | Depth Jitter; Depth avanzado | — | Con Depth Jitter = 0 y Depth sin curva, cambiar el mínimo no debe alterar el resultado. | **No implementado** |
| Depth Jitter | Variación de profundidad | Oscila al azar entre textura y color base. Requiere Moving. | no documentado | Depth Minimum | Ninguno | Varianza del contraste local entre sellos > 0. | **No implementado** |
| Offset Jitter | Variación de desplazamiento | Cambia en cada trazo dónde cae la textura. Apagado, el patrón es constante. | Interruptor o deslizador (no queda claro) | Requiere Moving | Ninguno | Dos trazos idénticos: patrón idéntico con el jitter apagado y distinto con él activo. | **No implementado** |
| Blend Mode | Modo de fusión del grano | Cómo se mezcla el grano con el color base. Funciona con Moving y con Texturized. | Lista de modos no enumerada en la página | Depth | Ninguno | Comparar con fórmulas de fusión de referencia (Multiply, etc.) sobre un grano conocido. | **No implementado** |
| Brightness | Brillo | Aclara u oscurece el grano. | no documentado | Contrast | Ninguno | Media de luminancia del grano contra el parámetro. | **No implementado** |
| Contrast | Contraste | Ajusta la diferencia entre claros y oscuros del grano. | no documentado | Brightness | Ninguno | Desviación típica del grano contra el parámetro. | **No implementado** |
| Grain Filtering | Filtrado del grano | Mismas 3 opciones que Shape Filtering. | No / Classic / Improved | Scale | Ninguno | Aliasing (energía de alta frecuencia) del grano a escala pequeña. | **No implementado** |
| 3D Grain Behavior → Grain follows camera | El grano sigue a la cámara | Apagado repite el patrón de forma uniforme; encendido lo desplaza con el ángulo de cámara (pintura 3D). | Interruptor | Solo 3D | — | Fuera de alcance (Trazo es 2D). | **No implementado** |
| Grain Editor → Auto Repeat | Repetición automática | Controles: Grain Scale, Rotate, Border Overlap, Mask Hardness, Mirror Overlap y Pyramid Blending. La vista previa es en vivo; con dos dedos se invierten los tonos. | 6 controles; rangos no documentados | Grain Source | — | Generar un mosaico a partir de una imagen sin costura: el gradiente en el borde debe ser continuo (sin escalón > umbral). | **No implementado** |

## 6. Rendering (Renderizado): 9 subparámetros

| Parámetro (EN) | Traducción | Qué hace (doc.) | Rango/opciones | Interacciones | Hardware | Prueba propuesta | Estado en Trazo |
|---|---|---|---|---|---|---|---|
| Rendering Mode | Modo de renderizado | Modelo de acumulación y mezcla de pintura. De más ligero a más pesado: Light Glaze (el estándar, como pintura diluida), Uniformed Glaze (parecido al de Photoshop), Intense Glaze, Heavy Glaze (conserva la opacidad al mezclar), Uniform Blending (estilo Photoshop con más mezcla húmeda) e Intense Blending (el más pesado, para pinceles húmedos que arrastran y mezclan). | 6 opciones (doc.) | Flow, Wet Mix, Opacidad, Blend Mode | Ninguno | Para cada modo: alfa acumulado tras N pasadas en el mismo trazo y en trazos distintos, con flujo 50 %. Curvas α(N) por modo (requiere observación). | **Parcial.** Hay un único modelo: los sellos se acumulan en un búfer de trazo y se componen al final con la opacidad (el alfa dentro del trazo nunca supera la opacidad, comportamiento cercano a un «glaze»). No hay modos «blending». |
| Flow | Flujo | Cuánto color y textura deja el pincel. | Porcentaje; no documentado | Rendering Mode, Pressure → Flow | P (vía Apple Pencil → Flow) | Alfa de un sello aislado contra Flow; alfa de saturación en un trazo largo. | **Implementado.** `Flow` multiplica el alfa de cada sello. Falta la calibración frente a %. |
| Wet Edges | Bordes húmedos | Suaviza y desenfoca los bordes, como pigmento que se corre en el papel. | no documentado | Bleed | Ninguno | Ancho del gradiente de borde (del 10 % al 90 % de alfa) contra el parámetro. | **No implementado** |
| Burnt Edges | Bordes quemados | Efecto de subexposición de color (color burn) en los bordes donde se solapan los trazos. | no documentado | Burnt Edges Mode | Ninguno | Oscurecimiento medido solo en la franja de borde solapada. | **No implementado** |
| Burnt Edges Mode | Modo de bordes quemados | Modo de fusión aplicado solo al efecto de borde. | Lista no enumerada | Burnt Edges | Ninguno | Comparar modos sobre un fondo de color fijo. | **No implementado** |
| Blend Mode | Modo de fusión | Modo de fusión de todo el trazo. | Lista no enumerada en la página (remite a Blend Modes) | Rendering Mode, opacidad | Ninguno | Pintar sobre una rampa de color y compararlo con fórmulas de referencia. | **No implementado.** Solo hay normal, alpha lock (SourceAtop) y borrador. |
| Luminance Blending | Fusión por luminancia | Mezcla valores de luminosidad en vez de color. | Interruptor | Blend Mode | Ninguno | Superponer rojo y azul de igual luminancia y comprobar si el resultado coincide con el modelo de luminancia (requiere observación). | **No implementado** |
| Alpha Threshold | Umbral alfa | Cada píxel queda totalmente opaco o totalmente transparente según un umbral, útil para pixel art. | Deslizador; no documentado | Filtrado, Flow | Ninguno | Histograma de alfa del trazo: solo valores {0, 255} (y saber si el umbral es ≥ o >). | **No implementado** |
| Classic Normal Combine mode | Combinación Normal clásica | Devuelve al modo Normal de los Dual Brush el comportamiento anterior a 5.4. | Interruptor | Dual Brush | Ninguno | Solo aplica si se implementa Dual Brush. | **No implementado** |

## 7. Wet Mix (Mezcla húmeda): 8 subparámetros

| Parámetro (EN) | Traducción | Qué hace (doc.) | Rango/opciones | Interacciones | Hardware | Prueba propuesta | Estado en Trazo |
|---|---|---|---|---|---|---|---|
| Dilution | Dilución | Cantidad de agua en la pintura: más agua, más transparencia. | no documentado | Charge, Wetness Jitter | ¿P? (no documentado) | Alfa de un trazo sobre blanco contra Dilution: monótono decreciente. | **No implementado** |
| Charge | Carga | Pintura cargada al empezar el trazo. Se agota durante el trazo y se recarga al levantar. | no documentado | Dilution, Attack | Ninguno | Alfa o cobertura contra la longitud recorrida: debe decaer; tras levantar, el siguiente trazo debe recuperarla. | **No implementado** |
| Attack | Ataque | Cuánta pintura se adhiere al lienzo; con valor alto la pintura es gruesa y uniforme. Tiene ajustes avanzados de azimut, inclinación y barrel roll (este último solo en Pencil Pro). | no documentado | Charge, Pull | Az, I, BR (doc.) | Cobertura media en una pasada contra Attack. | **No implementado** |
| Pull | Arrastre | Fuerza con que el pincel arrastra la pintura existente (y la nueva). | no documentado | Rendering Mode, Blur | Ninguno | Pintar azul sobre una franja roja: medir la distancia de contaminación roja en la dirección del trazo. | **No implementado.** La herramienta Difuminar es independiente y no forma parte del pincel. |
| Grade | Gradación | Grado de «grumosidad» y contraste de la textura del pincel. | no documentado | Grain | Ninguno | Contraste local del trazo contra Grade. | **No implementado** |
| Blur | Desenfoque | Desenfoque que se aplica a la pintura del lienzo y alcance de ese desenfoque. | no documentado | Blur Jitter, Pull | Ninguno | Pintar sobre un borde nítido y medir el ancho del borde resultante. | **No implementado** |
| Blur Jitter | Variación de desenfoque | Aleatoriza el desenfoque en cada sello. | no documentado | Blur | Ninguno | Varianza del ancho de borde entre sellos > 0. | **No implementado** |
| Wetness Jitter | Variación de humedad | Aleatoriza la mezcla de agua durante el trazo. | no documentado | Dilution | Ninguno | Varianza del alfa entre sellos > 0 con Dilution fija. | **No implementado** |

**Ambigüedad:** el manual no aclara si Wet Mix solo actúa con ciertos Rendering Modes, ni si Dilution, Pull o Charge pueden vincularse a presión (solo menciona los ajustes avanzados de Attack).

## 8. Color Dynamics (Dinámica de color): 22 subparámetros (5 grupos)

«Secondary Color» (color secundario) significa que el color oscila entre el primario y el secundario elegidos en el panel de color.

| Grupo (EN) | Traducción | Subparámetros (doc.) | Qué hace (doc.) | Rango/opciones | Interacciones | Hardware | Prueba propuesta | Estado en Trazo |
|---|---|---|---|---|---|---|---|---|
| Stamp Color Jitter | Variación de color por sello | Hue, Saturation, Lightness, Darkness, Secondary Color (5) | Cada sello toma al azar una variación de matiz, saturación, claridad u oscuridad, o un color entre primario y secundario. | Porcentajes; no documentado | Se suma a Stroke Jitter y Pressure | Ninguno | Con semilla fija, el histograma HSL de los sellos debe tener un ancho proporcional al %; Darkness solo debe bajar L. | **No implementado** (los 5) |
| Stroke Color Jitter | Variación de color por trazo | Hue, Saturation, Lightness, Darkness, Secondary Color (5) | Lo mismo, pero un único valor aleatorio por trazo. | Porcentajes; no documentado | Igual que el anterior | Ninguno | Varianza de color dentro de un trazo ≈ 0 y entre trazos > 0. | **No implementado** (los 5) |
| Color Pressure | Color por presión | Hue, Saturation, Brightness, Secondary Color (4) | La presión desplaza el color. En Brightness, de blanco a negro según el manual (sentido ambiguo). | Porcentajes; no documentado | Curva de presión | P (doc.) | Rampa de presión de 0 a 1: el color de los sellos debe seguir una curva monótona en el canal correspondiente. | **No implementado** (los 4) |
| Color Tilt | Color por inclinación | Hue, Saturation, Lightness, Secondary Color (4) | La inclinación desplaza el color. | Porcentajes; no documentado | Umbral de inclinación | I (doc.) | Igual que el anterior, con un barrido de inclinación. | **No implementado** (los 4) |
| Color barrel roll | Color por giro del barril | Hue, Saturation, Lightness, Secondary Color (4) | El giro del barril desplaza el color. | Porcentajes; no documentado | Relative to stroke | BR (doc.) | Igual que el anterior, con un barrido de giro (Twist en Windows). | **No implementado** (los 4) |

**Ambigüedad:** el manual mezcla Lightness, Brightness y Darkness. No dice en qué espacio de color se aplica el jitter (HSB, HSL u otro) ni si el matiz da la vuelta (wrap) en 360°.

## 9. Dynamics (Dinámica): 5 subparámetros

| Parámetro (EN) | Traducción | Qué hace (doc.) | Rango/opciones | Interacciones | Hardware | Prueba propuesta | Estado en Trazo |
|---|---|---|---|---|---|---|---|
| Speed → Size | Tamaño por velocidad | En −100 %, ir despacio da un trazo más fino; en +100 %, ir despacio lo engrosa (sentido según el manual). 0 % es uniforme. | −100 % a +100 % (doc.) | Presión → Size; StreamLine (¿velocidad cruda o suavizada?) | V (doc.) | Misma trayectoria a 3 velocidades: el diámetro medio debe ser monótono en la dirección que indique el signo. | **No implementado** |
| Speed → Opacity | Opacidad por velocidad | Igual, aplicado a la opacidad. | −100 % a +100 % (doc.) | Flow | V (doc.) | Igual, midiendo el alfa. | **No implementado** |
| Speed → Spacing | Espaciado por velocidad | El espaciado aumenta con la velocidad. Es nuevo en 5.4. | no documentado | Spacing, Spacing Jitter | V (doc.) | Número de sellos por 100 px contra la velocidad: decreciente. | **No implementado** |
| Jitter → Size | Variación de tamaño | Tamaño aleatorio por sello (no depende de la velocidad); útil para pintar con el dedo. | no documentado | Size, Presión | Ninguno | Histograma de diámetros con semilla fija: ancho ∝ jitter. | **Implementado.** `Jitter` aplica un factor uniforme 1 ± Jitter. Falta calibrar la escala en %. |
| Jitter → Opacity | Variación de opacidad | Opacidad aleatoria por sello. | no documentado | Flow | Ninguno | Histograma del alfa de los sellos. | **No implementado** |

**Ambigüedad:** la página resumida describe el signo de Speed → Size de forma algo contradictoria (−100 % aparece como «despacio = más fino» en la cita literal, mientras que el resumen general lo describía al revés). **Debe verificarse por observación.**

## 10. Apple Pencil: 17 subparámetros

| Parámetro (EN) | Traducción | Qué hace (doc.) | Rango/opciones | Interacciones | Hardware | Prueba propuesta | Estado en Trazo |
|---|---|---|---|---|---|---|---|
| Pressure Graph | Gráfico de presión | Curva por pincel: el eje X es la presión del lápiz y el eje Y el efecto. Admite hasta 4 nodos extra; al tocar un nodo se borra o se reinicia el gráfico. Cada ajuste de presión tiene su propia curva y su «response speed». El ajuste global (Prefs → Pressure & Smoothing) sigue aplicándose. | Hasta 4 nodos extra (doc.); el tipo de interpolación no está documentado | Todos los ajustes vinculados a presión; curva global | P (doc.) | Curva con nodos conocidos: la salida en presiones intermedias debe coincidir con la interpolación esperada (lineal, spline… según lo que se observe). La composición global∘pincel debe verificarse. | **Parcial.** Solo hay una curva global gamma (`PressureCurve.Apply`). Falta la curva por pincel y por ajuste, con nodos y response speed. |
| Pressure → Size | Tamaño por presión | La presión modula el tamaño. | Porcentaje; no documentado | Gráfico de presión, Taper | P | Diámetro contra presión en 11 puntos (0, 0.1 … 1). | **Implementado** (lineal con mínimo `MinSizePressure`). Falta la curva propia. |
| Pressure → Opacity | Opacidad por presión | La presión modula la opacidad. | Porcentaje; no documentado | Rendering Mode | P | Alfa máximo alcanzable dentro del trazo contra presión (opacidad como tope, no como acumulación). | **Parcial.** `FlowPressure` (etiquetado «Opacidad por presión» en la UI) modula el alfa por sello, que es flujo y se acumula. No hay tope de opacidad por presión. |
| Pressure → Flow | Flujo por presión | La presión modula el flujo. | Porcentaje; no documentado | Flow | P | Alfa de un sello aislado contra presión. | **Implementado** (`FlowPressure`, lineal) |
| Pressure → Bleed | Sangrado por presión | Cuánto se corre el pincel en sus bordes según la presión. | Porcentaje; no documentado | Wet Edges | P | Ancho del borde contra presión. | **No implementado** |
| Tilt Graph | Gráfico de inclinación | El nodo azul fija el ángulo a partir del cual actúa la inclinación. Rango 0–90°: de 0 a 15° el lápiz no toca; de 16 a 30° puede ser impreciso; de 30 a 90° es el rango recomendado. Según la página Brush Studio, el ajuste por defecto es 9°. | 0–90° (doc.) | Todos los ajustes vinculados a inclinación | I (doc.) | Barrido de ángulo: sin efecto por debajo del umbral y con efecto por encima. **Ojo con la convención:** el manual parece medir el ángulo respecto a la superficie (0° = tumbado), mientras que Trazo mide desde la vertical. | **No implementado** |
| Tilt → Opacity | Opacidad por inclinación | La inclinación modula la opacidad. | no documentado | Tilt Graph | I | Alfa contra ángulo. | **No implementado** |
| Tilt → Gradation | Gradación | Suaviza el pincel al sombrear inclinado, como un lápiz de grafito. | no documentado | Tilt Graph | I | Dureza de borde contra ángulo. | **No implementado** |
| Tilt → Bleed | Sangrado por inclinación | Aumenta el sangrado de bordes al inclinar, a costa del detalle. | no documentado | Wet Edges | I | Ancho del borde contra ángulo. | **No implementado** |
| Tilt → Size | Tamaño por inclinación | La inclinación modula el tamaño. | no documentado | Tilt Graph, Size Compression | I | Diámetro contra ángulo. | **Parcial.** `size × (1 + Tilt·t²)`, con t = magnitud desde la vertical / 70°. No tiene umbral ni curva, y la convención de ángulo está invertida respecto al manual. |
| Tilt → Size Compression | Compresión de tamaño | Evita que la textura crezca con el tamaño del pincel al inclinar. | Interruptor (inferido) | Grain, Tilt → Size | I | Período del grano constante al variar la inclinación. | **No implementado** |
| Barrel roll → Size | Tamaño por giro | El giro del barril modula el ancho; 0 % da ancho constante. Admite «Relative to stroke». | Porcentaje (doc.) | Input Style | BR (doc.) | Diámetro contra Twist. | **No implementado** |
| Barrel roll → Opacity | Opacidad por giro | De None a un %. | None…% (doc.) | — | BR | Alfa contra Twist. | **No implementado** |
| Barrel roll → Bleed | Sangrado por giro | De None a un %. | None…% (doc.) | — | BR | Borde contra Twist. | **No implementado** |
| Cursor Outline | Contorno del cursor | None, Contrast (escala de grises que se adapta al fondo) o Active color. Requiere el estilo de contorno «Per Brush» en Prefs → Advanced. | 3 opciones (doc.) | Preferencia global | Hover del lápiz | Verificar las 3 opciones visualmente. | **Parcial.** Hay un cursor circular global de dos tonos (equivale a «Contrast»), no configurable por pincel. Además es un círculo y no sigue la forma de la punta. |
| Hover → Estimated pressure | Presión estimada (hover) | Fija qué cantidad de pigmento se muestra en el cursor al pasar por encima, de Disabled a Max (100 %). | Disabled…Max (doc.) | Hover fill | Hover del lápiz | Previsualización del cursor con alfa ∝ parámetro. | **No implementado** |
| Hover → Hover fill | Relleno en hover | None, Shape (color sólido) o All (textura y color). | 3 opciones (doc.) | Cursor Outline | Hover del lápiz | Verificar las 3 opciones visualmente. | **No implementado** |

## 11. Properties (Propiedades): 8 subparámetros

| Parámetro (EN) | Traducción | Qué hace (doc.) | Rango/opciones | Interacciones | Hardware | Prueba propuesta | Estado en Trazo |
|---|---|---|---|---|---|---|---|
| Use Stamp Preview | Previsualizar como sello | En la biblioteca muestra el pincel como un sello en vez de como un trazo. También aparece en Preview. | Interruptor | Preview | — | Miniatura de un único sello centrado. | **No implementado.** La miniatura siempre es un trazo. |
| Orient to Screen | Orientar a la pantalla | Solo afecta a pinceles con un arriba y un abajo claros. Apagado sigue el lienzo; encendido sigue la orientación del dispositivo. | Interruptor | Rotación del lienzo | Orientación del dispositivo | Girar la vista 90°: con la opción apagada, la punta se ve girada en pantalla; encendida, no. | **Parcial.** Los sellos se orientan al lienzo (equivale a «apagado»). Falta la opción. |
| Preview Size | Tamaño de vista previa | Tamaño de la previsualización en la biblioteca. | no documentado | — | — | Solo UI. | **No implementado.** El tamaño de la miniatura es fijo y derivado de `Size`. |
| Smudge Pull | Arrastre al difuminar | Intensidad al usar el pincel con la herramienta Difuminar. | no documentado | Wet Mix → Pull (no se documenta la relación) | — | Distancia de arrastre de color contra el parámetro. | **No implementado.** La intensidad está fijada en el código (depende de la opacidad). |
| Brush Behavior → Maximum Size | Tamaño máximo | Límite superior del deslizador de tamaño. | Unidades no documentadas | Deslizador lateral | — | El deslizador al 100 % debe dar exactamente el máximo. | **Implementado** (`MaxSize`, en px de documento) |
| Brush Behavior → Minimum Size | Tamaño mínimo | Límite inferior del deslizador de tamaño. | Unidades no documentadas | Igual que el anterior | — | El deslizador al 0 % debe dar el mínimo. | **Implementado** (`MinSize`) |
| Brush Behavior → Maximum Opacity | Opacidad máxima | Límite superior del deslizador de opacidad. | no documentado | Deslizador lateral | — | Igual que en tamaño. | **No implementado** |
| Brush Behavior → Minimum Opacity | Opacidad mínima | Límite inferior del deslizador de opacidad. | no documentado | Igual que el anterior | — | Igual que en tamaño. | **No implementado** |

## 12. Materials (Materiales, pintura 3D): 4 subparámetros

| Parámetro (EN) | Traducción | Qué hace (doc.) | Rango/opciones | Interacciones | Hardware | Prueba propuesta | Estado en Trazo |
|---|---|---|---|---|---|---|---|
| Metallic → Amount | Metalizado (cantidad) | Aspecto metálico del trazo en 3D. | 0 % (no metálico) a 100 % (metálico) (doc.) | Metallic Source | — | Fuera de alcance (Trazo es 2D). | **No implementado** (fuera de alcance) |
| Metallic → Source | Fuente de metalizado | Textura en grises: el blanco deja ver el metalizado y el oscuro lo bloquea. Incluye importación, Auto Repeat y Scale. | Textura | Amount | — | Fuera de alcance | **No implementado** (fuera de alcance) |
| Roughness → Amount | Rugosidad (cantidad) | Va de brillante a mate. | 0 % (brillante) a 100 % (mate) (doc.) | Roughness Source | — | Fuera de alcance | **No implementado** (fuera de alcance) |
| Roughness → Source | Fuente de rugosidad | Textura en grises, análoga a Metallic Source. | Textura | Amount | — | Fuera de alcance | **No implementado** (fuera de alcance) |

## 13. Preview (Vista previa, nueva en 5.4): 6 subparámetros (sin contar «Use Stamp Preview», ya contado en Properties)

| Parámetro (EN) | Traducción | Qué hace (doc.) | Rango/opciones | Interacciones | Hardware | Prueba propuesta | Estado en Trazo |
|---|---|---|---|---|---|---|---|
| Preview (trazo de muestra) | Trazo de muestra | Trazo de demostración con presión creciente que deja ver los efectos ligados a la presión. | — | Todos los parámetros de presión | — | La miniatura debe mostrar un diámetro creciente de izquierda a derecha en un pincel con Size por presión. | **Parcial.** `DrawPreview` dibuja una sinusoide con presión `sin(πt)` (sube y baja, no crece de forma monótona). |
| Size | Tamaño | Tamaño del trazo de muestra. | no documentado | — | — | Solo UI. | **No implementado** |
| Pressure Minimum | Presión mínima | Presión inicial de la muestra; en el máximo, la presión es uniforme. | no documentado | Pressure Scale | — | Comprobar que, en el máximo, el diámetro es constante. | **No implementado** |
| Pressure Scale | Escala de presión | Escala de la presión de la muestra. | no documentado | Pressure Minimum | — | Solo UI. | **No implementado** |
| Wet Mix | Mezcla húmeda en la muestra | Activa o desactiva Wet Mix en la miniatura. | Interruptor | Wet Mix | — | Solo UI. | **No implementado** |
| Tilt Angle | Ángulo de inclinación | Inclinación simulada en la muestra, para ver efectos de azimut y barrel roll. | no documentado | Input Style | — | Solo UI. | **No implementado** |

## 14. About this Brush (Acerca de este pincel): 6 subparámetros

| Parámetro (EN) | Traducción | Qué hace (doc.) | Rango/opciones | Interacciones | Hardware | Prueba propuesta | Estado en Trazo |
|---|---|---|---|---|---|---|---|
| Author Picture | Foto del autor | Imagen importada, visible para quien reciba el pincel. | Imagen | Exportación del pincel | — | Ida y vuelta de serialización. | **No implementado** |
| Made by (Name) | Autor | Nombre del creador. | Texto | — | — | Ida y vuelta de serialización. | **No implementado** |
| Date Created | Fecha de creación | Se rellena automáticamente. | Fecha | — | — | Comprobar que se fija al crear y no cambia al editar. | **No implementado** |
| Signature | Firma | Firma manuscrita (con lápiz o dedo), incrustada en el archivo .brush. | Trazo vectorial o bitmap (no documentado) | — | — | Ida y vuelta de serialización. | **No implementado** |
| Create New Reset Point | Crear punto de restauración | Guarda un punto de restauración. Solo hay uno activo por pincel. | Acción | Reset brush | — | Crear el punto, editar y restaurar: los ajustes deben volver a los del punto. | **No implementado** |
| Reset brush | Restablecer pincel | Vuelve al punto de restauración o, si no hay ninguno, al estado original. | Acción | Create New Reset Point | — | Editar y restablecer: los ajustes deben ser los de fábrica. | **Parcial.** El botón «Restablecer pincel» llama a `CopySettingsFrom(Default)`, pero solo restaura 7 campos (StreamLine, Spacing, Flow, Jitter, Scatter, MinSizePressure y FlowPressure) y no admite un punto de restauración propio. |

Nota: desde 5.4, el título del pincel se cambia desde la biblioteca (pulsación larga), no desde el Brush Studio.

---

## 15. Elementos transversales del Brush Studio (fuera de los 14 atributos): 8 elementos

| Elemento (EN) | Traducción | Qué hace (doc.) | Rango/opciones | Prueba propuesta | Estado en Trazo |
|---|---|---|---|---|---|
| Advanced settings → Numeric | Entrada numérica | Al tocar el valor de un ajuste se puede escribir un número exacto (con teclado o Scribble). | — | Introducir un valor y comprobar que el modelo guarda exactamente ese valor. | **No implementado** (solo deslizadores) |
| Advanced settings → Pressure | Presión por ajuste | Asigna una curva de presión a ese ajuste (gráfico de 4×4, desactivado por defecto, hasta 4 puntos extra). | Hasta 4 puntos extra (doc.) | Igual que Pressure Graph, para cualquier parámetro | **No implementado** |
| Advanced settings → Tilt | Inclinación por ajuste | Vincula el ajuste al ángulo de inclinación. | Por defecto 9°, ajustable de 0 a 90° (doc.) | Barrido de inclinación | **No implementado** |
| Advanced settings → Barrel Roll | Giro por ajuste | Vincula el ajuste al barril (solo Pencil Pro). «Relative to stroke» está activado por defecto. | Interruptor «Relative to stroke» | Barrido de Twist | **No implementado** |
| Drawing Pad | Zona de pruebas | Lienzo de prueba en vivo dentro del estudio: se limpia con tres dedos, restablece ajustes, cambia el tamaño de la muestra y ofrece 8 colores de prueba. | — | Solo UI | **No implementado.** El panel de ajustes solo regenera la miniatura. |
| Dual Brush → Combine | Combinar | Une dos pinceles del mismo set (primario y secundario) en uno que usa la forma y el grano de ambos. No se pueden combinar Dual Brushes. | — | Render del combinado frente a la composición manual de los dos trazos | **No implementado** |
| Dual Brush → Combine Mode | Modo de combinación | Modo de fusión entre los dos pinceles; por defecto Normal. La página no enumera la lista y remite a Blend Modes. | Lista no enumerada | Por modo, comparar con la fórmula de fusión | **No implementado** |
| Dual Brush → Uncombine | Separar | Separa el pincel combinado en sus dos componentes. | — | Ida y vuelta: separar y recombinar debe dar ajustes idénticos | **No implementado** |

---

## 16. Recuento

| Atributo | Subparámetros | Implementado | Parcial | No implementado |
|---|---|---|---|---|
| 1. Stroke Path | 5 | 0 | 3 | 2 |
| 2. Stabilization | 6 | 0 | 1 | 5 |
| 3. Taper | 13 | 0 | 0 | 13 |
| 4. Shape | 15 | 1 | 4 | 10 |
| 5. Grain | 16 | 0 | 0 | 16 |
| 6. Rendering | 9 | 1 | 1 | 7 |
| 7. Wet Mix | 8 | 0 | 0 | 8 |
| 8. Color Dynamics | 22 | 0 | 0 | 22 |
| 9. Dynamics | 5 | 1 | 0 | 4 |
| 10. Apple Pencil | 17 | 2 | 4 | 11 |
| 11. Properties | 8 | 2 | 1 | 5 |
| 12. Materials | 4 | 0 | 0 | 4 |
| 13. Preview | 6 | 0 | 1 | 5 |
| 14. About this Brush | 6 | 0 | 1 | 5 |
| **Total (14 atributos)** | **140** | **7** | **16** | **117** |
| Transversales (sección 15) | 8 | 0 | 0 | 8 |

Incluso los «Implementado» (Scatter, Flow, Jitter Size, Pressure Size, Pressure Flow, Max/Min Size) carecen de una **calibración** frente a la escala de Procreate, porque esa escala no está documentada.

---

## 17. Comportamientos que requieren observación directa (preguntas abiertas)

Ninguna de estas preguntas tiene respuesta en el manual. Hay que medirlas en un iPad con Procreate 5.4.x, grabando la entrada si es posible (o al menos capturas con trazos patrón), y documentarlas en `docs/research/` como observaciones, con la fecha y la versión.

**Trayectoria y espaciado**
1. ¿Cómo se pasa del Spacing en % a la distancia entre sellos? ¿Lineal, exponencial? ¿Relativa al diámetro actual o al nominal? ¿Cuánto vale el «espaciado mínimo interno» que menciona el manual?
2. Spacing Jitter: ¿qué distribución sigue (uniforme o gaussiana) y es simétrica? ¿Cómo se combina con Speed → Spacing?
3. Jitter Lateral y Linear: ¿qué amplitud máxima tienen al 100 %, en diámetros? ¿Qué distribución siguen?
4. Fall Off: ¿qué curva sigue (lineal, exponencial), en función de la distancia o del número de sellos, y a qué afecta (opacidad, flujo o tamaño)?

**Estabilización**
5. ¿Qué algoritmo concreto usan StreamLine, Stabilization y Motion Filtering? ¿Qué ventana o constante temporal tienen? ¿Se ve afectado por el zoom? ¿En qué orden se aplican si hay varios activos?
6. ¿Cómo pondera Stabilization la velocidad?
7. StreamLine → Pressure: ¿es un filtro de primer orden sobre la presión? ¿Cuál es su constante?
8. ¿Qué tiene prioridad, la estabilización global (Accesibilidad) o la del pincel?
9. ¿Qué pasa al final del trazo? ¿Se «alcanza» el último punto real (como hace Trazo en `End()`) o el trazo se queda corto?

**Taper**
10. ¿Qué forma exacta tiene la curva de afinado (lineal, ease-in, potencia)? ¿Se mide en px de pantalla, de lienzo o en % del trazo?
11. ¿Cómo se afina el final si el trazo no ha terminado? ¿Retroactivamente o con latencia?
12. Trazos más cortos que la suma de los dos afinados: ¿se reescalan?
13. ¿Cómo se combinan Taper → Size, Opacity y Tip? ¿Cómo interactúa Taper → Pressure con la curva de presión? ¿Qué cambia Classic Taper?

**Forma**
14. ¿En qué orden se aplican flip, rotación base, Rotation (seguir el trazo), azimut, Scatter y Randomized?
15. ¿Cuántos grados son Scatter al 100 %? ¿Count Jitter a valores intermedios da un rango [?, Count]?
16. ¿El aplastamiento de la redondez es un escalado anisótropo en el eje local de la forma? ¿Cómo se componen redondez por presión, por inclinación y los jitters?
17. ¿Qué filtro usa cada modo de Shape Filtering (mipmaps, supersampling)? ¿Cuál es el tamaño mínimo de sello?

**Grano**
18. Moving frente a Texturized: ¿en qué espacio vive el grano (de lienzo, de pantalla o de trazo)? Con Texturized, ¿el grano está anclado al lienzo y se ve afectado por el zoom de la vista?
19. ¿Cómo interactúan Scale y Zoom (Cropped ↔ Follow Size)? ¿El período del grano es `Scale × tamaño` o `Scale × constante`?
20. Moving: ¿cómo avanza el desplazamiento del grano según el recorrido (Movement)? ¿Es un desplazamiento por sello proporcional al espaciado?
21. ¿Cuál es la fórmula de Depth, Depth Minimum y Depth Jitter? ¿Qué Blend Mode usa el grano por defecto?
22. ¿Qué algoritmos usa Auto Repeat (Pyramid Blending, Mirror Overlap)?

**Renderizado y mezcla**
23. ¿Cuál es el modelo matemático de cada uno de los 6 Rendering Modes? ¿Hay tope de opacidad por trazo, acumulación entre sellos y mezcla con el color existente? ¿Light Glaze equivale a «búfer de trazo + opacidad máx.»?
24. Wet Edges, Burnt Edges y Bleed: ¿qué filtro usa cada uno y sobre qué capa actúa?
25. ¿Qué fórmula usa Luminance Blending? ¿Qué espacio de color?
26. Alpha Threshold: ¿el umbral se aplica por sello o sobre el trazo acumulado?

**Wet Mix**
27. ¿Qué modelo de mezcla usa (recogida de color del lienzo, depósito, agotamiento)? ¿Cómo se agota Charge (curva frente a distancia)?
28. ¿Qué relación hay entre Pull y Smudge Pull? ¿Y entre Wet Mix y Rendering Mode?
29. ¿Cómo se combinan Dilution y Flow?

**Color**
30. ¿En qué espacio se aplican los jitters (HSB o HSL)? ¿El matiz da la vuelta? ¿Cuál es la amplitud al 100 %?
31. ¿Qué dirección y escala tienen Color Pressure → Brightness?

**Hardware**
32. ¿Qué tipo de interpolación usa el Pressure Graph (lineal, Bézier, Catmull-Rom)? ¿Cómo se compone con la curva global? ¿Qué es exactamente «response speed»?
33. ¿Qué convención de ángulo usa la inclinación (desde la superficie o desde la vertical)? ¿El umbral de 9° por defecto se mide en esa misma convención?
34. ¿Qué signo tiene Speed → Size? Las fuentes resumidas se contradicen.
35. ¿Cómo se calcula la velocidad (px de pantalla/s o de lienzo/s) y cómo se suaviza?
36. Al usar el dedo, ¿cuál es la presión efectiva (1.0 o un valor fijo intermedio)?

**Dual Brush**
37. ¿Qué lista de Combine Modes hay? ¿Qué diferencia exacta hay entre el Normal clásico y el de 5.4?

---

## 18. Prioridad propuesta para Trazo

> Es una propuesta de ingeniería de Trazo, no del fabricante. El criterio es el impacto en el núcleo de pintura (cómo se ve y se siente un trazo), dividido por el coste.

**P0. Fundamentos de la señal de entrada (lo demás depende de esto)**
1. **Modelo de entrada completo:** presión, inclinación con una convención explícita (documentar la diferencia con Procreate), azimut (`atan2(YTilt, XTilt)`), Twist (barrel roll, si el digitalizador lo ofrece) y velocidad. Hoy solo se usan la presión y la magnitud de la inclinación.
2. **Curvas por ajuste** (Pressure Graph y ajustes avanzados de Pressure y Tilt): una abstracción `Curve` con nodos y un `Binding(input → parámetro)` reutilizable. Esto desbloquea Pressure Opacity, Bleed, Tilt *, Color Pressure, etc.
3. **Taper** (presión y táctil): ahora mismo un trazo con el dedo no tiene ningún afinado. Es la brecha más visible al dibujar líneas.

**P1. Calidad del trazo**
4. **Grain independiente de la punta** (Moving/Texturized, Scale, Zoom, Depth): es la base de cualquier pincel texturizado. Exige separar la máscara de forma de la máscara de grano en `BrushEngine`.
5. **Stroke Path:** separar los ejes Lateral y Linear de `Scatter`, Spacing Jitter, Fall Off y calibrar el Spacing.
6. **Shape:** importar puntas, redondez (gráfico, presión e inclinación), Rotation de −100 a +100 %, Count y Count Jitter, Flip, Randomized y Shape Filtering con mipmaps.
7. **Rendering:** opacidad como tope de trazo frente a flujo como acumulación (corregir la semántica de `FlowPressure`), Blend Mode por pincel, Alpha Threshold y, más adelante, los 6 Rendering Modes.
8. **Stabilization:** añadir Stabilization (promedio dependiente de la velocidad), Motion Filtering y el suavizado de presión de StreamLine.

**P2. Expresividad**
9. **Dynamics:** Speed → Size, Opacity y Spacing; Jitter → Opacity.
10. **Color Dynamics:** jitters por sello y por trazo, y color por presión e inclinación (coste bajo una vez existan los bindings del punto 2).
11. **Wet Mix y Smudge Pull:** recogida de color, Charge y Dilution. Es de coste alto y necesita observación previa (preguntas 27–29).
12. **Wet Edges, Burnt Edges y Bleed.**

**P3. Gestión y UI**
13. Properties (límites de opacidad y Orient to Screen), Preview y Drawing Pad, About this Brush (metadatos y punto de restauración), entrada numérica.
14. Dual Brush (Combine Mode).

**Fuera de alcance:** Materials y 3D Grain Behavior (Trazo es 2D).

### Las 5 brechas más importantes (resumen)
1. **Grain inexistente:** la textura está horneada en la punta y no hay Moving ni Texturized.
2. **Taper inexistente:** ni por presión ni táctil; los trazos con el dedo salen sin afinar.
3. **Sin curvas ni vínculos por ajuste:** solo hay una gamma global y dos vínculos lineales fijos (tamaño y flujo). Falta el Pressure Graph y faltan los vínculos de inclinación, azimut y barrel roll.
4. **Un único modelo de render**, sin Blend Mode por pincel, sin Rendering Modes y sin distinguir opacidad (tope) de flujo (acumulación).
5. **Estabilización y jitters de trayectoria incompletos:** solo hay StreamLine «cuerda» sin suavizar la presión; no hay Stabilization ni Motion Filtering; Scatter es isótropo en lugar de Lateral y Linear, y no hay Spacing Jitter ni Fall Off.
