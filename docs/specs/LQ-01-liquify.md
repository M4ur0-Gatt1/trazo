# LQ-01 — Licuar

**Estado:** en desarrollo · **Fecha:** 2026-10-25
**Código:** `src/Trazo.Core/Imaging/Liquify.cs`, `src/Trazo.Rendering/ImageTools.cs` (`LiquifySession`) · **Pruebas:** `LQ01_*` en las dos suites

## 1. Referencia (Procreate, documentado)

Licuar (*Ajustes › Licuar*) deforma la capa pintando con un pincel. Modos: **Empujar**, **Girar a la derecha**, **Girar a la izquierda**, **Pellizcar**, **Expandir**, **Cristales**, **Borde** y **Reconstruir**, con *Ajustar* (reconstruye todo de forma gradual) y *Restablecer*. Parámetros: tamaño, presión, distorsión e impulso. El manual no documenta las fórmulas.

## 2. Modelo (decisiones de Trazo)

- **Campo de desplazamiento** u(p) en una cuadrícula de 4 px (interpolación bilineal entre nodos): la imagen resultante es `salida(p) = original(p − u(p))`, con remuestreo bilineal en alfa premultiplicado y transparencia fuera del lienzo. Se calcula en la CPU de forma determinista y solo en la zona tocada.
- **Cada toque** (*dab*, uno cada 25 % del radio a lo largo del trazo) desplaza localmente el contenido δ(p) y el campo se compone de forma exacta: `u′(p) = δ(p) + u(p − δ(p))`. Así, empujar dos veces 5 px equivale a empujar una vez 10 px.
- **Caída:** w(r) = (1 − (r/R)²)² para r < R, 0 fuera. Fuera del radio los nodos del campo no cambian **nunca**; por la interpolación entre nodos, la imagen puede cambiar como mucho 1,5 celdas (6 px) más allá.
- **Modos** (c = centro del toque, k = presión × intensidad):

| Modo | δ(p) |
|---|---|
| Empujar | w · (c − c_anterior) |
| Girar a la derecha / izquierda | R(±θ·w·k)(p − c) − (p − c), con θ = 0,3 rad por toque |
| Pellizcar | −0,25 · k · w · (p − c) (el contenido va hacia el centro) |
| Expandir | +0,25 · k · w · (p − c) |
| Reconstruir | no desplaza: u′ = u · (1 − 0,5 · k · w) |

- **Ajustar** escala el campo entero (0 % = original, 100 % = todo lo hecho). **Restablecer** pone el campo a cero.
- **Selección y máscaras:** Licuar trabaja sobre la textura activa (capa o máscara) y se aplica con la misma regla de selección que el resto (SE-01).
- Aplicar es un único paso de historial.

## 3. Criterios de aceptación

| ID | Criterio | Prueba |
|---|---|---|
| LQ-01.1 | Campo cero: salida idéntica byte a byte | `ZeroFieldIsIdentity` |
| LQ-01.2 | Empujar con un radio enorme traslada el contenido exactamente (en la meseta) | `PushTranslatesInPlateau` |
| LQ-01.3 | Composición: dos empujes de 5 px = uno de 10 px (± 0,05 px en el campo) | `PushComposes` |
| LQ-01.4 | Localidad: a más de R + 1,5 celdas ningún píxel cambia, y todo lo que cambia está en el rectángulo que devuelve el toque | `DabIsLocal` |
| LQ-01.4b | La composición es la definición: un solo campo con dos toques equivale a deformar dos veces seguidas (diferencia media < 0,8 niveles donde actúa el segundo; con la fórmula rota, 1,1–4,5) | `ComposedFieldEqualsSequentialWarps` |
| LQ-01.5 | Pellizcar reduce y Expandir aumenta el área de una mancha | `PinchShrinksExpandGrows` |
| LQ-01.6 | Girar mueve el contenido en el sentido indicado | `TwirlDirection` |
| LQ-01.7 | Reconstruir repetido vuelve al original | `ReconstructRestoresOriginal` |
| LQ-01.8 | Ajustar al 0 % devuelve el original y al 100 % no cambia nada | `AdjustScalesField` |
| LQ-01.9 | En la GPU: la vista previa coincide con lo aplicado; respeta la selección; se deshace exactamente | `LiquifyApplyMatchesPreview`, `LiquifyRespectsSelectionAndUndo` (WARP) |
| LQ-01.10 | Referencia revisada a ojo de una cuadrícula licuada | `LiquifyMatchesReference` (WARP) |

## 4. Fuera de esta iteración

Cristales, Borde, impulso (*momentum*) y el parámetro de distorsión aleatoria.
