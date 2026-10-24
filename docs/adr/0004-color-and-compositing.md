# ADR-0004 — Color y composición (decisión D)

**Estado:** provisional — falta la gestión de color y medir la precisión · **Fecha:** 2026-10-24

## Estado actual (explícito)

| Aspecto | Valor actual |
|---|---|
| Espacio de color | **sRGB implícito, sin gestión de color**: los valores se tratan como sRGB y se envían al monitor sin perfil |
| Precisión interna | 8 bits por canal (`B8G8R8A8UIntNormalized`) |
| Alfa | **Premultiplicado** en la GPU; PNG sin premultiplicar en disco |
| Mezcla | Direct2D compone en el espacio de los valores almacenados (gamma sRGB), **no** en espacio lineal |
| Modos de fusión | Los 16 de `BlendEffect` de Direct2D, más SourceOver, DestinationOut (borrar) y SourceAtop (bloqueo alfa y recorte) |
| Perfiles, Display P3 y CMYK | No existen |

## Riesgos conocidos

1. **Bandas** en degradados suaves y en aerógrafos de flujo bajo: 8 bits y acumulación de sellos con alfa bajo.
2. **Redondeo al guardar:** premultiplicado → PNG → premultiplicado pierde hasta 1 nivel en píxeles casi transparentes (medido en LY-01).
3. La mezcla en gamma sRGB oscurece las transiciones entre colores saturados. Es habitual en apps de pintura, pero hay que decidirlo de forma explícita.

## Opciones a evaluar

| Opción | Coste |
|---|---|
| Capas en 16 bits flotante (`R16G16B16A16Float`) | El doble de memoria; acaba con las bandas; permite mezcla lineal |
| Mantener 8 bits y acumular el trazo en un búfer de 16 bits | Poca memoria extra; mejora los aerógrafos |
| Gestión de color con perfil del documento (sRGB / Display P3) y conversión al monitor | Requiere ICC (Windows Color System) |

## Medidas necesarias antes de decidir

- Pruebas de composición con valores conocidos: comparar cada modo de fusión con la fórmula de referencia (W3C Compositing and Blending) en una rejilla de colores.
- Contar los niveles distintos en un degradado pintado con aerógrafo suave a flujo 0,1, para cuantificar las bandas.
- Error de ida y vuelta de un archivo en función del alfa.
