# RC-01 — Recuperación tras fallos

**Estado:** en desarrollo · **Fecha:** 2026-10-25
**Código:** `src/Trazo.Core/IO/SessionJournal.cs`, `src/Trazo.App/Views/StudioView.Recovery.cs`, `src/Trazo.App/App.xaml.cs` · **Pruebas:** `RC01_*`, autoprueba y `tools/crash-test.ps1`

## 1. Objetivo

Ningún fallo debe costar más que unos segundos de trabajo, y la app debe decir con claridad qué pasó y qué se conservó. Se cubren tres casos:

| Caso | Ejemplo | Antes de RC-01 |
|---|---|---|
| **Cierre inesperado** del proceso | Corte de luz, Windows mata la app, fallo nativo | El autoguardado conservaba el trabajo, pero la app no avisaba al volver a abrirla |
| **Pérdida del dispositivo gráfico** | Actualización o reinicio del controlador, cambio de GPU, suspensión | Las capas viven en la GPU: el documento quedaba inservible y el autoguardado fallaba en bucle |
| **Excepción inesperada** en la interfaz | Un error de programación | Se registraba y la app seguía, sin proteger la obra |

## 2. Decisiones

### Diario de sesión (cierre inesperado)

- Al abrir un documento se escribe `sessions/session-<pid>.json` en la carpeta de datos de la app, con el proceso (pid y hora de inicio), la ruta del documento, la hora de apertura y la del **último guardado correcto**.
- Al cerrar el documento o la app de forma normal, el archivo se borra.
- Al arrancar, un diario cuyo proceso ya no existe (comprobado por pid **y** hora de inicio, para que un pid reutilizado no engañe) es una **sesión abandonada**. La app muestra «Trazo se cerró inesperadamente. «X» está guardada hasta las HH:MM:SS» con el botón *Abrir*, y borra el diario.
- Los diarios de otras instancias que siguen abiertas no se tocan. Un diario dañado se borra sin avisar y se anota en el registro.
- La pérdida máxima sigue siendo la de FI-01.8: **3 s** de trabajo (el intervalo del autoguardado) más lo que tarde el guardado en curso.

### Pérdida del dispositivo gráfico

- **Disparo:** el evento `DeviceLost` del dispositivo de Win2D (lo provoca el lienzo al detectar el error al dibujar) o una excepción no controlada cuyo código es de pérdida de dispositivo.
- **Recuperación:** se descartan todas las texturas, sesiones y cachés ligadas a la GPU anterior (pinceles, granos, miniaturas, vistas previas). Después se **vuelve a abrir el documento desde el último guardado** en el dispositivo nuevo, con el mismo zoom y la misma posición, y se avisa: «La tarjeta gráfica se reinició. Se recuperó la obra hasta el último guardado (hace N s).»
- Se pierden el historial de deshacer y lo hecho desde el último guardado. No hay forma de leer las texturas de un dispositivo perdido.
- Si el documento aún no tiene archivo o no se puede abrir, la app vuelve a la galería y lo explica.
- **Limitación de prueba:** Win2D solo permite disparar `DeviceLost` cuando el dispositivo está realmente perdido (llamarlo con un dispositivo sano lanza `ArgumentException`, comprobado). Por eso se prueba **la recuperación completa** (reabrir, cachés nuevas y seguir pintando) llamándola directamente, y el disparo se revisa en el código.

### Excepción inesperada

- Se registra con su pila.
- Si es una pérdida de dispositivo, va a la recuperación anterior.
- Si no, y hay cambios sin guardar, se guarda una **copia de emergencia** «Nombre (recuperado).trazo» junto al documento, **sin tocar el archivo principal** (el estado podría estar dañado), y se avisa. Como mucho una copia por minuto.
- Las excepciones de otros hilos y de tareas no observadas también se registran.

## 3. Criterios de aceptación

| ID | Criterio | Prueba |
|---|---|---|
| RC-01.1 | Un diario de un proceso que ya no existe se detecta como abandonado, con su documento y su último guardado | `AbandonedSessionIsDetected` |
| RC-01.2 | El diario de un proceso vivo no se toca, aunque haya otros abandonados | `LiveSessionIsIgnored` |
| RC-01.3 | Un pid reutilizado por otro proceso (otra hora de inicio) cuenta como abandonado | `ReusedPidIsAbandoned` |
| RC-01.4 | Cerrar bien borra el diario; un diario dañado se descarta sin fallar | `CleanEndRemovesJournal`, `CorruptJournalIsDiscarded` |
| RC-01.5 | Guardar actualiza la hora del último guardado | `SaveUpdatesLastSaved` |
| RC-01.6 | Tras un «cierre» sin guardar al final, el documento se abre con todo lo guardado (motor real) | `CrashKeepsLastSavedWork` (WARP) |
| RC-01.7 | La recuperación de la GPU reabre el documento con la misma imagen, cachés nuevas, y permite seguir pintando | Autoprueba |
| RC-01.8 | La copia de emergencia no modifica el archivo principal y se limita a una por minuto | `EmergencyCopyLeavesPrimaryUntouched` |
| RC-01.9 | Proceso real: pintar, esperar al autoguardado, **matar el proceso** y reabrir: aviso de sesión abandonada y trazo presente | `tools/crash-test.ps1` |
