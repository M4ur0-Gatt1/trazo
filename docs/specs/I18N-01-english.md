# I18N-01 — Interfaz en inglés

**Estado:** implementado · **Fecha:** 2026-10-26
**Código:** `src/Trazo.Core/Localization/` (catálogo y traducción), `src/Trazo.App/Services/Loc.cs` (extensión de marcado para XAML) · **Pruebas:** `I18N01_*` en `Trazo.Core.Tests` y la autoprueba con `TRAZO_LANG=en`

## 1. Referencia

Procreate está traducido a una docena de idiomas, y la mayoría de quienes dibujan en tabletas Windows no hablan español. El inglés es el primer idioma que hace falta: abre la app a casi todo el mercado de la Store. Windows publica el idioma de la interfaz del usuario en `CultureInfo.CurrentUICulture`.

## 2. Decisiones

| Aspecto | Decisión |
|---|---|
| Idiomas | Español (el original) e inglés |
| Elección | Acciones → Preferencias → «Idioma»: *Automático* (por defecto), *Español* o *English*. Automático usa español si el idioma de Windows es español y, si no, inglés. El cambio se aplica al reiniciar Trazo, y así se avisa |
| Claves | Estilo *gettext*: la clave es el propio texto en español. Así un texto sin traducir se ve en español en lugar de romperse, y los nombres que pone el usuario (capas, pinceles propios, grupos) pasan sin cambios |
| Catálogo | `src/Trazo.Core/Localization/en.json` (texto español → texto inglés), incrustado en el ensamblado y leído con `JsonDocument` (sin reflexión, compatible con el recorte de .NET) |
| Código | `Tr.T("texto")` para textos fijos y `Tr.F("{0} capas", n)` para textos con valores. Los números siguen el formato regional de Windows, como el resto de la interfaz (decimales con coma o punto); el signo de porcentaje sí sigue el idioma («50 %» / «50%») |
| XAML | `{t:Loc S='texto'}`, una extensión de marcado que traduce al cargar la vista |
| Qué se traduce | Todo lo que ve el usuario: menús, botones, ajustes, avisos, diálogos, nombres de pinceles, modos de fusión, formas de QuickShape, tamaños de lienzo, nombres por defecto («Capa 3» → «Layer 3») y mensajes de error de los formatos (PSD, `.trazo`) |
| Qué no | Registros (`trazo.log`), autoprueba y prueba de cierre forzado (archivos `*SelfTest*` y `*CrashTest*`) y comprobaciones internas, que son para desarrollo; cada línea interna lleva `// i18n: interno` para que la excepción se vea en el código. Tampoco los nombres de producto (Trazo, QuickShape, StreamLine) ni los nombres de idioma, que se escriben en su propio idioma |
| Textos ambiguos | Una misma palabra con dos sentidos lleva dos claves: «Difuminar» (herramienta, *Smudge*) y «Difuminar borde» (selección, *Feather*); en Licuar, «Cantidad» en lugar de «Ajustar» (que en Transformar es *Fit*) |
| Instalador y Store | Fuera de esta iteración |

## 3. Criterios de aceptación

| ID | Criterio | Prueba |
|---|---|---|
| I18N-01.1 | Cada texto marcado con `T`, `F` o `{t:T}` en el código y en el XAML tiene traducción inglesa no vacía | `EveryMarkedStringHasEnglish` |
| I18N-01.2 | El catálogo no tiene entradas que ya no se usan | `CatalogHasNoStaleEntries` |
| I18N-01.3 | Los marcadores `{0}`, `{1}`… de cada traducción son los mismos que los del original | `PlaceholdersMatch` |
| I18N-01.4 | Ningún texto en español queda sin marcar en la interfaz (búsqueda en el código de la app, con una lista explícita de excepciones: registros, autoprueba e identificadores) | `NoUnmarkedSpanishInUi` |
| I18N-01.5 | Sin traducción se devuelve el original; `F` formatea con la cultura del idioma; el idioma automático sigue a Windows | `TranslationFallsBackAndFormats`, `AutomaticFollowsWindows` |
| I18N-01.6 | Los nombres de los 16 modos, los pinceles de la biblioteca, las formas y los tamaños de lienzo tienen traducción | `LibraryNamesAreTranslated` |
| I18N-01.7 | La app arranca y completa la autoprueba en inglés, y la captura no muestra textos en español | Autoprueba con `TRAZO_LANG=en` (69/69) y capturas revisadas de pinceles, selección y capas, Licuar y Acerca de |
