# ADR-0001 — Representación del lienzo (decisión A)

**Estado:** provisional — pendiente de la comparación medida con mosaicos · **Fecha:** 2026-10-24

## Contexto

Cada capa necesita guardar sus píxeles y permitir actualizar regiones pequeñas (un trazo), componer todo a 60 fps, deshacer y guardar.

## Decisión actual

**Un bitmap completo por capa en la GPU** (`CanvasRenderTarget` de Win2D, BGRA de 8 bits premultiplicado y del tamaño del lienzo). Se compone con un grafo de efectos de Direct2D en cada fotograma.

## Alternativas

| Opción | A favor | En contra |
|---|---|---|
| Bitmap completo por capa (**actual**) | Sencillo; Direct2D compone muy rápido; los modos de fusión son nativos | La memoria crece con el tamaño del lienzo aunque la capa esté casi vacía (64 MP × 4 B = 256 MB por capa); límite de textura de 16 384 px |
| Mosaicos (p. ej. 256 × 256) con mosaicos vacíos dispersos | Memoria proporcional al contenido; lienzos sin límite práctico; deshacer y guardar por mosaico | Más complejo: composición por mosaico, bordes de los sellos entre mosaicos, más llamadas de dibujo |
| Híbrido: mosaicos en CPU + caché en GPU | Lienzos enormes; memoria acotada | Mayor complejidad y transferencias CPU↔GPU |

## Qué hay que medir antes de decidir (PF-01)

1. Memoria de vídeo y RAM con lienzos de 4K, A3 a 300 ppp y 64 MP, con 10, 50 y 100 capas mayormente vacías.
2. Tiempo por fotograma componiendo N capas con modos de fusión, en GPU integrada (Intel/Qualcomm) y dedicada.
3. Coste de actualizar una región de 64 × 64 px y de copiarla para deshacer.
4. Tiempo de guardado de un documento grande.

**Criterio:** si el bitmap completo supera el presupuesto de memoria (≤ 2 GB de vídeo para el caso típico) o de fotograma (≤ 8 ms de composición) en una GPU integrada, se migra a mosaicos.

## Consecuencias

- `CanvasLimits.MaxLayers` limita las capas con un presupuesto de 2 GB.
- El historial ya guarda solo el rectángulo modificado (ver ADR-0003), así que un cambio a mosaicos no lo afectaría.
