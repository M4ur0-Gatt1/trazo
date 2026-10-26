# AD-02 — Curvas, mapa de degradado, equilibrio de color, desenfoque de movimiento y resplandor

**Estado:** implementado · **Fecha:** 2026-10-26
**Código:** `src/Trazo.Core/Imaging/ToneMaps.cs` (curvas, degradados y equilibrio como tablas de 256 valores, sin GPU), `src/Trazo.Rendering/ImageTools.cs` (efectos de Direct2D) · **Pruebas:** `AD02_*` en las dos suites y la autoprueba

## 1. Referencia

Procreate tiene en *Ajustes* curvas por canal, mapa de degradado con biblioteca, equilibrio de color por sombras, medios tonos y luces, desenfoque de movimiento y resplandor (*bloom*). Trazo ya tenía tono/saturación/brillo, contraste, exposición, temperatura, desenfoque gaussiano, enfocar, ruido, invertir, escala de grises, sepia y opacidad (sin especificación propia: AD-01 queda como el conjunto anterior).

## 2. Decisiones

| Ajuste | Comportamiento |
|---|---|
| **Curvas** | Curva compuesta (gamma) y una por canal (rojo, verde, azul), con el mismo editor y la misma interpolación monótona que las curvas de presión (BR-06): hasta 8 puntos, doble toque para añadir o quitar. Se aplica primero la compuesta y después la del canal |
| **Mapa de degradado** | La luminancia (Rec. 709) de cada píxel elige un color del degradado. Biblioteca de 7 degradados, uno de ellos «Color actual» (negro → color actual → blanco). *Intensidad* mezcla con el original e *Invertir* da la vuelta al degradado |
| **Equilibrio de color** | Tres ejes (cian–rojo, magenta–verde, amarillo–azul) para sombras, medios tonos y luces. Cada canal se corrige con los pesos de Bernstein (1−v)², 2v(1−v) y v², que suman 1, así que un desplazamiento igual en las tres zonas mueve el canal entero por igual. Desplazamiento máximo: ±0,3 |
| **Desenfoque de movimiento** | Desenfoque direccional con ángulo (0–360°) y distancia |
| **Resplandor** | Las luces por encima de un umbral se desenfocan y se suman en modo *Trama* sobre la imagen. Parámetros: umbral, tamaño e intensidad. No sale fuera de la forma de la capa |

- **Una sola implementación:** las curvas, el degradado y el equilibrio se calculan en el núcleo como tablas de 256 valores por canal, y la GPU solo las aplica (efecto de tabla de Direct2D). Así la matemática se prueba sin GPU y la GPU se compara contra la misma tabla.
- **Alfa:** las tablas actúan sobre el color sin premultiplicar; la transparencia de la capa no cambia.
- **Selección:** como el resto de ajustes, solo cambian la zona seleccionada (SE-01).

## 3. Criterios de aceptación

| ID | Criterio | Prueba |
|---|---|---|
| AD-02.1 | Curvas: sin puntos son la identidad; la del canal se aplica después de la compuesta; las tablas son monótonas si los puntos lo son | `CurvesComposeAndStayMonotonic` |
| AD-02.2 | Degradado: interpolación lineal entre paradas, extremos fijos, invertir da la vuelta; los 7 degradados de la biblioteca son válidos | `GradientSamplesStopsAndInverts` |
| AD-02.3 | Equilibrio: en cero es la identidad; un mismo desplazamiento en las tres zonas mueve el canal por igual; las sombras apenas mueven las luces y al revés | `ColorBalanceWeightsAreTonal` |
| AD-02.4 | En la GPU, curvas, mapa de degradado y equilibrio dan lo mismo que la tabla del núcleo aplicada en la CPU (± 2 niveles), también en píxeles semitransparentes, y no cambian el alfa | `TableAdjustmentsMatchCpuReference` (WARP) |
| AD-02.5 | El desenfoque de movimiento horizontal extiende una línea vertical en horizontal y no en vertical, y conserva la tinta: no gana nada y pierde menos del 10 % (las colas más débiles redondean a 0 en 8 bits) | `MotionBlurFollowsAngle` (WARP) |
| AD-02.6 | El resplandor no cambia una imagen sin luces sobre el umbral, aclara alrededor de las luces y no pinta fuera de la forma de la capa | `BloomBrightensOnlyAroundHighlights` (WARP) |
| AD-02.4b | Regresión: la intensidad intermedia de Invertir y Escala de grises mezcla hacia el original (antes salía al revés) | `IntermediateIntensityMixesTowardsOriginal` (WARP) |
| AD-02.7 | Todos los ajustes respetan la selección y se deshacen en un paso | Autoprueba en GPU real (7 comprobaciones nuevas, 76/76) y capturas de los tres editores |
