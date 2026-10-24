# ADR-0003 — Historial (decisión C)

**Estado:** aceptada (híbrido); falta medir la memoria con documentos grandes · **Fecha:** 2026-10-24

## Decisión

**Historial híbrido** con una sola pila (`UndoStack`, en Trazo.Core):

| Tipo de operación | Estrategia | Ejemplo |
|---|---|---|
| Píxeles en una región | **Parche de píxeles**: copia del rectángulo modificado antes y después, en la GPU | Trazo, relleno, difuminar |
| Píxeles en toda la capa | Parche del rectángulo completo | Ajustes, transformar, invertir |
| Estructura | **Comando reversible** que conserva las referencias a las capas | Añadir, eliminar, mover, duplicar |
| Propiedades | Comando con valor anterior y nuevo; **solo** las continuas (deslizadores) se fusionan si llegan en menos de 1,2 s | Opacidad (se fusiona), visibilidad (no) |
| Operación autoinversa | Comando que se reaplica | Voltear el lienzo |
| Combinar abajo | Instantánea de la capa inferior antes y después | — |

Límites: 150 pasos y 1,5 GB. Al superarlos se descartan los pasos más antiguos y se liberan sus texturas (`IDisposable`).

## Alternativas descartadas

- **Solo instantáneas de capa:** 4 bytes × píxeles por paso; inasumible con lienzos grandes.
- **Solo comandos, repitiendo los trazos al deshacer:** exige que el motor sea 100 % determinista entre versiones y vuelve el deshacer lento en documentos con mucho trabajo.

## Verificado

LY-01 deshace y rehace 16 operaciones comparando hashes en cada paso. Además encontró y fijó un error: los interruptores se fusionaban (HI-01.3).

## Pendiente

- Medir la memoria del historial con trazos grandes en un lienzo de 64 MP (PF-01).
- Operaciones interrumpidas: una excepción a mitad de una operación no debe dejar una acción a medias en la pila.
- Las capas eliminadas siguen referenciadas por el historial; su memoria no se cuenta en el presupuesto.
