# Documentación técnica de Trazo

Esta carpeta es la base técnica verificable del proyecto. Rige una regla:
**cada función tiene una especificación, cada especificación tiene pruebas y cada error corregido deja una prueba de regresión.**

| Documento | Contenido |
|---|---|
| [reference.md](reference.md) | Paso 1: qué se estudia (versión de Procreate, dispositivos, alcance) |
| [requirements.md](requirements.md) | Matriz de requisitos con el estado de las 5 puertas de cierre por módulo |
| [research/brush-studio-inventory.md](research/brush-studio-inventory.md) | Inventario del Brush Studio de Procreate 5.4: 14 atributos y 140 subparámetros |
| [specs/](specs/) | Especificaciones por módulo: [BR-03 grano](specs/BR-03-grain.md), [BR-05 afinado](specs/BR-05-taper.md) |
| [protocols/](protocols/) | Protocolos de prueba BR-01, BR-02, LY-01 y FI-01, con la correspondencia a las pruebas automáticas |
| [adr/](adr/) | Decisiones de arquitectura A–E: lienzo, motor de pinceles, historial, color y persistencia |
| [results/](results/) | Resultados de ejecuciones conservados, con fecha y métricas |

## Cómo se cierra un módulo

Un módulo solo se considera terminado cuando supera las cinco puertas. Marcar una casilla no ejecuta nada: cada puerta remite a una evidencia.

| Puerta | Evidencia exigida |
|---|---|
| 1. Investigación | Fuentes citadas; se separa lo **documentado** de lo que **requiere observación** |
| 2. Especificación | Requisitos con identificador, datos, estados, dependencias y criterios de aceptación |
| 3. Implementación | Código integrado en `main` |
| 4. Pruebas | Pruebas automáticas en `tests/`, ejecutadas en CI, con los resultados conservados en `results/` |
| 5. Aprobación | Criterios de aceptación superados y limitaciones conocidas registradas |

## Ejecutar las pruebas

```bash
dotnet test Trazo.slnx
```

- `tests/Trazo.Core.Tests`: lógica pura (trayectoria, historial, formato, relleno, vista). No necesita GPU.
- `tests/Trazo.Rendering.Tests`: el motor real de Direct2D sobre **WARP**, el renderizador por software de Direct3D. Es determinista, así que funciona igual en cualquier PC y en el CI. Las imágenes de referencia están en `tests/Trazo.Rendering.Tests/Golden/`.

Para regenerar las referencias después de un cambio **intencionado** del motor:

```bash
TRAZO_UPDATE_GOLDEN=1 dotnet test tests/Trazo.Rendering.Tests
```

Antes de confirmar las nuevas referencias hay que revisarlas a ojo con `tools/contact-sheet.ps1` y explicar el cambio en el commit.
