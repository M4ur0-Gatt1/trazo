# TL-01 — Grabación en time-lapse

**Estado:** implementado · **Fecha:** 2026-10-26
**Código:** `src/Trazo.Core/IO/TimelapseFile.cs` (archivo de fotogramas y plan de exportación, sin GPU), `src/Trazo.Rendering/TimelapseRecorder.cs` (captura) y `src/Trazo.Rendering/TimelapseVideo.cs` (vídeo MP4) · **Pruebas:** `TL01_*` en las dos suites y la autoprueba

## 1. Referencia

Procreate graba cada obra mientras se pinta, sin que haya que activar nada, y permite verla dentro de la app (*Repetición del time-lapse*) y exportarla como vídeo completo o resumido en 30 segundos. Es una de las funciones más usadas para compartir el proceso en redes.

## 2. Decisiones

| Aspecto | Decisión |
|---|---|
| Cuándo se graba | Cada cambio del historial (trazo, relleno, ajuste, deshacer, rehacer…) programa una captura; los cambios seguidos se agrupan y se captura como mucho un fotograma cada 200 ms, siempre con el estado final del grupo. Un fotograma igual al anterior no se guarda |
| Qué se graba | La imagen aplanada (con el fondo; sobre blanco si está oculto), reducida para que el lado mayor mida como mucho 1080 px y con lados pares, como pide H.264 |
| Dónde | Un archivo junto a la obra: `obra.trazo.timelapse`. No forma parte del `.trazo`, así que no hace más lento el guardado verificado (FI-01) ni cambia su formato. Al renombrar, duplicar o eliminar una obra desde la galería, su time-lapse la acompaña |
| Formato | Cabecera `TRZTL1` con el tamaño del fotograma y, después, cada fotograma como JPEG (calidad 0,85) precedido de su longitud y del tiempo transcurrido. Solo se añade al final: si la app se cierra a mitad de un fotograma, al leer se descarta ese final incompleto y lo anterior sigue valiendo |
| Si cambia el tamaño | Si el lienzo cambia de proporción (por ejemplo, al recortarlo), los fotogramas nuevos se encajan en el tamaño del primero con bandas |
| Activar | Acciones › Preferencias › «Grabar time-lapse» (activado por defecto). Lo ya grabado se conserva al desactivarlo |
| Ver | Acciones › Lienzo › «Time-lapse…»: reproductor con reproducir/pausa, barra para moverse, número de fotogramas, tiempo de trabajo y tamaño en disco |
| Si falla | Un error al grabar nunca interrumpe el trabajo: se anota en el registro y se deja de grabar hasta volver a abrir la obra. Un archivo de time-lapse dañado se aparta como `.dañado` y se empieza otro |
| Exportar | Desde el reproductor: **vídeo completo** (todos los fotogramas a 30 por segundo) o **30 segundos** (se eligen fotogramas repartidos por igual para que quepa). En los dos, la obra terminada se queda 2 s al final. MP4 H.264 con Media Foundation de Windows |
| Tamaño en disco | Unos 50–150 kB por fotograma; se muestra en el reproductor |

## 3. Criterios de aceptación

| ID | Criterio | Prueba |
|---|---|---|
| TL-01.1 | El archivo guarda y devuelve los fotogramas en orden con su tiempo; se puede seguir añadiendo después de reabrirlo | `FramesRoundTripAndAppend` |
| TL-01.2 | Un archivo cortado a mitad de un fotograma conserva los fotogramas completos; uno con cabecera ajena o dañada se rechaza con `InvalidDataException` | `TruncatedTailIsDropped`, `MalformedFilesFailCleanly` |
| TL-01.3 | Tamaño del fotograma: lado mayor como mucho 1080, lados pares, misma proporción que el lienzo (± 1 px) | `FrameSizeFitsAndIsEven` |
| TL-01.4 | Plan de exportación: el completo usa todos los fotogramas; el de 30 s no pasa de 30 s, empieza en el primero y termina en el último, reparte por igual y repite el último 2 s; con pocos fotogramas no se inventan | `ExportScheduleFitsAndKeepsEnds` |
| TL-01.5 | Captura: varios cambios seguidos dan un solo fotograma con el estado final; un cambio que no altera la imagen no añade fotograma; desactivada no graba | `RecorderCoalescesAndSkipsDuplicates` (WARP) |
| TL-01.6 | El vídeo exportado es un MP4 que Windows abre, con el tamaño del fotograma y la duración esperada (± 0,1 s), cuyo primer y último fotograma muestran el principio y el final de la obra | `ExportsPlayableMp4` |
| TL-01.7 | Renombrar, duplicar y eliminar desde la galería también mueven, copian y eliminan el time-lapse | `SidecarFollowsProject` |
| TL-01.8 | En la app real: pintar graba fotogramas, el reproductor los muestra y la exportación produce el vídeo | Autoprueba en GPU real: cinco trazos con pausas dan cinco fotogramas por el temporizador de la app, y el MP4 se exporta; captura del reproductor |
