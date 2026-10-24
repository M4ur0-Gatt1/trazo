# ADR-0002 — Motor de pinceles por etapas (decisión B)

**Estado:** aceptada (separación trayectoria/estampado/composición); textura y mezcla pendientes · **Fecha:** 2026-10-24

## Decisión

El motor se divide en etapas independientes y comprobables por separado:

| Etapa | Responsabilidad | Ubicación | Pruebas |
|---|---|---|---|
| 1. Entrada | Muestras del dispositivo → documento; orden temporal; curva de presión | `StrokeBuilder.Add`, `PressureCurve`, `InputRecording` | BR-01 |
| 2. Trayectoria | Estabilización (StreamLine), QuickShape | `StreamLineSmoother`, `QuickShape` | BR-01, QuickShape |
| 3. Dinámicas y espaciado | Paso entre sellos; tamaño y flujo según presión e inclinación; variación con semilla | `StrokeStamper` | BR-01 |
| 3b. Afinado | Rampas de inicio y final; cola provisional hasta levantar el lápiz | `TaperSettings`, `StrokeBuilder` | BR-05 |
| 4. Simetría | Reflejos de cada sello | `Symmetry` | pruebas del núcleo |
| 5. Generación de la punta | Máscaras alfa deterministas | `TipGenerator` | pruebas del núcleo |
| 6. Textura (grano) | Moving por sello o Texturized sobre el trazo acumulado | `GrainGenerator` (Core), `StrokeRasterizer` (Rendering) | BR-03 |
| 7. Mezcla húmeda | **No existe** | — | BR-04 pendiente |
| 8. Composición | Sellos → búfer del trazo → capa, con la opacidad de la herramienta y el modo (normal, borrar, bloqueo alfa) | `Trazo.Rendering/BrushEngine` | BR-02 |

Las etapas 1 a 5 están en `Trazo.Core` sin dependencias gráficas. La 8 está en `Trazo.Rendering`. La GPU solo recibe una lista de `Dab` (x, y, tamaño, alfa, giro).

## Por qué

- Si cambia un algoritmo de trayectoria, BR-01 lo detecta sin renderizar nada.
- Si cambia el render, BR-02 lo detecta con imágenes de referencia.
- Las etapas 6 y 7 se insertarán entre la 5 y la 8 sin tocar las demás.

## Brechas conocidas (del [inventario](../research/brush-studio-inventory.md))

- La **opacidad** (tope del trazo) y el **flujo** (acumulación por sello) existen, pero las dinámicas de presión solo afectan al flujo. La interfaz lo llamaba "opacidad por presión"; ya se corrigió la etiqueta.
- Faltan el grano independiente (Moving/Texturized), el afinado (taper), las curvas por parámetro, las dinámicas de velocidad, el azimut, los modos de render por pincel, el pincel doble y la mezcla húmeda.
