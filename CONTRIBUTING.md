# Contribuir a Trazo

Gracias por querer mejorar Trazo. Este documento explica cómo compilarlo y probarlo, y qué se espera de un cambio.

## Compilar y probar

Requisitos: Windows 10 2004 o posterior, [.NET 10 SDK](https://dotnet.microsoft.com/download) y Git.

```
git clone https://github.com/M4ur0-Gatt1/trazo
cd trazo
dotnet test tests/Trazo.Core.Tests
dotnet test tests/Trazo.Rendering.Tests -p:Platform=x64
dotnet run --project src/Trazo.App -p:Platform=x64
```

- Si la carpeta del repositorio tiene una ruta muy larga, WinUI puede fallar al compilar. Usa una ruta corta o una unidad virtual (`subst T: C:\ruta\larga\trazo`).
- Las pruebas de render usan Direct3D WARP (por software) y funcionan sin tarjeta gráfica.
- La autoprueba de extremo a extremo usa la GPU real: `dotnet run --project src/Trazo.App -p:Platform=x64 -- --selftest ./selftest`.

## Cómo se trabaja aquí

Cada función sigue el mismo ciclo, descrito en [docs/README.md](docs/README.md):

1. **Especificación** en `docs/specs/`: qué hace, qué decisiones se tomaron y una tabla de criterios de aceptación con el nombre de la prueba de cada uno.
2. **Implementación**: la lógica pura en `Trazo.Core` (sin dependencias gráficas), el dibujo en `Trazo.Rendering` y la interfaz en `Trazo.App`.
3. **Pruebas** para cada criterio. Lo que dibuja se compara con imágenes de referencia en `tests/Trazo.Rendering.Tests/Golden/`.
4. **Resultados** en `docs/results/` y estado en la [matriz de requisitos](docs/requirements.md).

Reglas:

- **Cada error corregido deja una prueba que lo habría detectado**, y se anota en la tabla de regresiones de la matriz.
- **Las referencias visuales no se regeneran sin mirarlas.** Si un cambio las altera a propósito, ejecuta `TRAZO_UPDATE_GOLDEN=1 dotnet test tests/Trazo.Rendering.Tests -p:Platform=x64`, revisa cada imagen nueva junto a la anterior y explica el cambio en el *pull request*.
- Las advertencias del compilador son errores (`TreatWarningsAsErrors`).
- El código, los comentarios y la documentación están en español.
- No se añaden conexiones a internet ni telemetría: la [política de privacidad](https://m4ur0-gatt1.github.io/trazo/privacidad.html) promete que no las hay.

## Informar de errores y proponer ideas

Usa las [plantillas de *issues*](https://github.com/M4ur0-Gatt1/trazo/issues/new/choose). Para fallos de seguridad, sigue [SECURITY.md](SECURITY.md).

## Convivencia

Trata a los demás con respeto y escribe pensando en que te lea alguien que empieza. No se aceptan ataques personales, acoso ni discriminación; el responsable del proyecto puede ocultar comentarios o bloquear a quien no lo respete.

## Licencia

Al contribuir aceptas que tu aporte se publique con la [licencia MIT](LICENSE) del proyecto.
