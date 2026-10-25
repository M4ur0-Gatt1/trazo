# BR-04 — Mezcla húmeda

**Estado:** en desarrollo · **Fecha:** 2026-10-25
**Código:** `src/Trazo.Core/Brushes/WetMix.cs`, `src/Trazo.Rendering/BrushEngine.cs` · **Pruebas:** `BR04_*` en las dos suites

## 1. Referencia (Procreate, documentado)

Brush Studio › *Mezcla húmeda* (*Wet Mix*): **Dilución** (agua: el color se aclara y se vuelve más transparente), **Carga** (cuánta pintura lleva el pincel antes de secarse), **Ataque** (cuánta pintura se deposita), **Arrastre** (*Pull*: cuánto color del lienzo arrastra el pincel), además de Grado, Desenfoque y Variación de humedad. El manual describe el efecto, no las fórmulas; la respuesta exacta de Procreate debe observarse.

## 2. Modelo (decisiones de Trazo)

El pincel lleva un **depósito** de color R (sin premultiplicar) y una cantidad de pintura P. Para cada sello, en orden:

1. **Muestra:** color medio del **contenido de la capa al empezar el trazo** bajo el sello (9 puntos: centro y anillo de radio 0,35 × tamaño; media premultiplicada), con su alfa a. Leer la capa del inicio, y no la que se está pintando, hace el resultado determinista e independiente de la velocidad de la GPU.
2. **Arrastre U:** `R ← R + U · a · (muestra − R)`. Sobre lienzo transparente (a = 0) el color no cambia.
3. **Carga C:** `P = exp(−d / L)`, d = distancia recorrida, `L = 12 × tamaño × C / (1 − C)`; con C = 1 la pintura no se acaba.
4. **Alfa del sello:** `α′ = α × (1 − 0,8·Dilución) × (0,2 + 0,8·Ataque) × P`; el color del sello es R.

- Valores por defecto (Dilución 0, Carga 1, Ataque 1, Arrastre 0): la mezcla está desactivada y los sellos no cambian.
- Se aplica antes de la simetría, así que los reflejos llevan el mismo color.
- Solo pinta con Pintar (no con Borrar ni Difuminar). En una máscara mezcla grises.
- Los sellos provisionales de la cola de afinado usan el depósito actual sin avanzarlo; los definitivos avanzan el depósito al soltar.
- La lectura de la capa se hace por mosaicos de 256 px bajo demanda (cada mosaico se lee una vez por trazo), no la capa entera.

**Pinceles con mezcla húmeda por defecto:** Gouache (Arrastre 0,35, Carga 0,85, Dilución 0,1, Ataque 0,8), Óleo (0,5 · 0,92 · 0 · 0,9) y Acuarela (0,25 · 0,7 · 0,4 · 0,7). Sus referencias BR-02 cambian a propósito y se revisan a ojo.

## 3. Criterios de aceptación

| ID | Criterio | Prueba |
|---|---|---|
| BR-04.1 | Con los valores por defecto los sellos salen idénticos y sin color propio | `DefaultsAreInactive` |
| BR-04.2 | Arrastre 0: el color es siempre el del pincel | `NoPullKeepsBrushColor` |
| BR-04.3 | Arrastre 1 sobre un color uniforme: el sello toma ese color; con 0,5, la distancia al color del lienzo se reduce a la mitad en cada sello | `PullConvergesGeometrically` |
| BR-04.4 | Sobre lienzo transparente el color no cambia | `TransparentCanvasDoesNotTint` |
| BR-04.5 | La carga decae como exp(−d/L) (± 1 %); con carga 1 no decae | `ChargeDecaysExponentially` |
| BR-04.6 | Dilución y Ataque escalan el alfa según la fórmula | `DilutionAndAttackScaleAlpha` |
| BR-04.7 | Mirar la cola no avanza el depósito | `PeekDoesNotAdvance` |
| BR-04.8 | En la GPU: un trazo con arrastre que cruza una mancha azul sale azulado después de cruzarla, y el mismo trazo sin arrastre no | `WetStrokePicksUpCanvasColor` (WARP) |
| BR-04.9 | Mismo trazo y misma capa: mismo resultado byte a byte | `WetStrokeIsDeterministic` (WARP) |

## 4. Fuera de esta iteración

Grado, Desenfoque, Variación de humedad, la mezcla con el color de otras capas (*Sample all layers*) y el secado en el tiempo.
