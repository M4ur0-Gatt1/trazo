## Qué cambia y por qué

<!-- Describe el cambio y enlaza el issue si lo hay. -->

## Comprobaciones

- [ ] Hay especificación (o se actualizó la existente en `docs/specs/`) con criterios de aceptación.
- [ ] Cada criterio tiene una prueba; si se corrige un error, hay una prueba que lo habría detectado.
- [ ] `dotnet test tests/Trazo.Core.Tests` y `dotnet test tests/Trazo.Rendering.Tests -p:Platform=x64` pasan.
- [ ] Si cambió alguna imagen de referencia, la revisé junto a la anterior y explico por qué cambia.
- [ ] Actualicé `docs/requirements.md` y `docs/results/` si cambia el estado de un requisito.
- [ ] No añade conexiones a internet ni telemetría.
