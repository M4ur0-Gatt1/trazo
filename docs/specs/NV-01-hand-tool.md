# NV-01 — Mano para moverse por el lienzo

**Estado:** implementado · **Fecha:** 2026-11-04
**Código:** `StudioView.HandTool`, `StudioView.Input.cs` (`Canvas_PointerPressed`) · **Pruebas:** autoprueba y prueba con entrada real

## 1. Objetivo

El usuario pidió «una manito para moverme por el espacio». Ya se podía mover el lienzo de tres formas, pero ninguna tenía botón:

- con dos dedos;
- con el botón central del ratón;
- con Espacio + arrastrar.

Con un ratón o un lápiz, y sin teclado, no había forma de hacerlo.

## 2. Decisiones

**Cómo se activa**
- Botón de la mano en la barra superior, antes del pincel.
- Atajo **H**: activa o desactiva la mano.
- Mientras la mano está activa, los botones de pincel, difuminar y borrar se ven sin marcar, y no se dibuja el círculo del pincel.

**Qué hace cada entrada**

| Entrada | Con la mano activa |
|---|---|
| Lápiz | Arrastra el lienzo |
| Botón izquierdo del ratón | Arrastra el lienzo |
| Un dedo | Gesto de navegación: mueve |
| Dos dedos | Igual que siempre: acercan, alejan y giran, y el toque de dos dedos deshace |
| Cuentagotas, rellenar, transformar, seleccionar, licuar y editar guía | Tienen prioridad, como antes |

**Cómo se suelta**
- Eligiendo una herramienta de pintar (botón o atajo B, S o E).
- Volviendo a tocar la mano.
- Al tocar el pincel estando activa la mano, no se abre la biblioteca: solo se elige el pincel.

## 3. Criterios de aceptación

| # | Criterio | Prueba |
|---|---|---|
| NV-01.1 | La mano se activa (botón marcado y herramientas de pintar sin marcar), y elegir una herramienta de pintar la suelta | Autoprueba «la mano se activa y una herramienta de pintar la suelta» |
| NV-01.2 | Con la mano activa, arrastrar con el botón izquierdo mueve el lienzo y no dibuja | Prueba con entrada real (capturas antes y después) |
