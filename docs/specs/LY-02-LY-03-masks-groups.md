# LY-02 — Máscaras de capa · LY-03 — Grupos de capas

**Estado:** en desarrollo · **Fecha:** 2026-10-25

## 1. Referencia (Procreate, documentado)

- **Máscara de capa:** una máscara en escala de grises asociada a una capa. El negro oculta y el blanco muestra; permite ocultar sin borrar. Se puede pintar, invertir, aplicar (fusionar con la capa) y eliminar.
- **Grupos:** reúnen capas en una carpeta que se puede contraer, ocultar, mover y combinar en una sola capa.

El manual no documenta cómo se componen los modos de fusión dentro de un grupo.

## 2. Decisiones de Trazo

### Máscaras (LY-02)

| Aspecto | Decisión |
|---|---|
| Representación | Textura opaca en **gris** del tamaño del documento: blanco (255) = visible, negro (0) = oculto |
| Composición | capa visible = capa × luminancia(máscara), aplicada **antes** de la opacidad, el modo de fusión y el recorte |
| Edición | Tocar la miniatura de la máscara alterna entre editar la capa y editar la máscara. Con la máscara activa, todas las herramientas de píxeles trabajan sobre ella |
| Pintar en la máscara | El pincel pinta la **luminancia** del color actual (negro oculta, blanco muestra); el **borrador restaura blanco** (vuelve a mostrar) |
| Operaciones | Añadir (blanca), Invertir, Aplicar (la capa queda multiplicada por la máscara y la máscara se elimina), Eliminar |
| Bloqueo alfa y recorte | El recorte usa la capa base **ya enmascarada** |

### Grupos (LY-03)

| Aspecto | Decisión |
|---|---|
| Nivel | **Un nivel**, sin grupos anidados (como Procreate) |
| Representación | Cada capa tiene un `GroupId` opcional; el documento tiene la lista de grupos (nombre, opacidad, modo de fusión, visible, contraído) |
| Invariante | Las capas de un grupo son **contiguas** en la pila |
| Composición | **Aislada:** las capas del grupo se componen sobre transparente y el resultado se aplica con la opacidad y el modo del grupo |
| Mover capas | Una capa que se mueve **dentro** del rango de un grupo pasa a formar parte de él; si sale por un extremo, queda fuera |
| Operaciones | Crear grupo con la capa activa, Agrupar con la capa de abajo, Desagrupar, Combinar grupo (en una sola capa), Eliminar grupo, Renombrar, Opacidad, Fusión y Visible |

### Archivo

`.trazo` pasa a la **versión 3**: añade `layers/NNN-mask.png` y, en el manifiesto, `mask`, `groupId` y `groups`. Trazo sigue leyendo las versiones 1 y 2. Las betas anteriores **rechazan** los archivos v3 con un mensaje claro, en vez de abrirlos perdiendo las máscaras sin avisar.

## 3. Criterios de aceptación

| Req. | Criterio | Prueba |
|---|---|---|
| LY-02.1 | Máscara blanca: imagen idéntica a la de sin máscara | `WhiteMaskIsInvisible` |
| LY-02.2 | Máscara negra: la capa no se ve; el gris al 50 % da alfa ≈ 50 % | `MaskLuminanceControlsVisibility` |
| LY-02.3 | Pintar con la máscara activa cambia la máscara y **no** la capa | `PaintingTargetsMask` |
| LY-02.4 | Aplicar la máscara deja una imagen compuesta idéntica | `ApplyMaskPreservesComposite` |
| LY-02.5 | Invertir dos veces devuelve la máscara original | `InvertMaskTwice` |
| LY-03.1 | Grupo al 100 % y en modo normal: imagen idéntica a la de sin grupo | `NeutralGroupIsInvisible` |
| LY-03.2 | La opacidad y la visibilidad del grupo afectan a todas sus capas | `GroupOpacityAndVisibility` |
| LY-03.3 | Combinar grupo produce una sola capa con la misma imagen compuesta | `MergeGroupPreservesComposite` |
| LY-03.4 | Las capas de un grupo siguen contiguas tras cualquier secuencia de movimientos (prueba de propiedades con semilla) | `GroupsStayContiguousUnderRandomMoves` |
| LY-01 ext. | Las nuevas operaciones se deshacen y rehacen exactamente (hash) y sobreviven a guardar y reabrir | `MasksGroupsSelectionUndoRedoAndPersistence` (23 operaciones: cada una cambia el documento, registra un paso y se deshace y rehace con hash idéntico) |
| FI-01 ext. | Formato v3 de ida y vuelta; v2 sigue leyéndose; una versión futura se rechaza | `V3RoundTripWithMasksAndGroups` y otras |
