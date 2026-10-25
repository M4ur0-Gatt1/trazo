# Historial de versiones

Las notas de cada versión publicada están en [Releases](https://github.com/M4ur0-Gatt1/trazo/releases). El detalle técnico y las pruebas de cada cambio están en [docs/results](docs/results/).

## 1.0.0 — 2026-10-25

Primera versión estable.

- **Curvas de presión (BR-06)** para el tamaño, el flujo y la opacidad, con editor en Ajustes del pincel.
- **Opacidad por presión separada del flujo (BR-06):** un tope por trazo que no sube al superponer sellos. Se calcula en la CPU porque Direct2D no tiene una mezcla «máximo» fiable.
- **Mezcla húmeda (BR-04):** dilución, carga, ataque y arrastre del color de la capa; activada en el gouache, el óleo y la acuarela.

## 0.2.0-beta.5 — 2026-10-25

- Transformar en cuatro modos: libre, uniforme, distorsionar y deformar, con modo magnético (TR-01).
- Licuar: empujar, girar, pellizcar, expandir y reconstruir (LQ-01).
- Guías de dibujo (cuadrícula 2D, isométrica y perspectiva) con Asistente de dibujo (DG-01).

## 0.2.0-beta.4 — 2026-10-25

- Selección automática, a mano, rectángulo y elipse (SE-01).
- Máscaras de capa y grupos de capas (LY-02, LY-03). Formato `.trazo` versión 3.

## 0.2.0-beta.3 — 2026-10-24

- Corrección: el lienzo podía dejar de redibujarse al alejar mucho el zoom.
- Nuevo icono e identidad visual.

## 0.2.0-beta.2 — 2026-10-24

- Grano de papel en movimiento y texturizado (BR-03) y afinado de extremos (BR-05).
- Guardado verificado con copia de seguridad (FI-01).

## 0.2.0-beta.1 — 2026-10-24

- Primera beta de la app nativa (WinUI 3 y Win2D) con instalador.
