# Publicar Trazo en la Microsoft Store

La Store firma el paquete al publicarlo, así que no hace falta comprar un certificado y no aparece el aviso de SmartScreen. El paquete MSIX convive con el instalador `.exe` de GitHub: el código es el mismo y solo cambia el empaquetado.

## Lo que hay en el repositorio

| Pieza | Dónde |
|---|---|
| Manifiesto del paquete (identidad, logos, asociación de `.trazo`, `runFullTrust`) | `src/Trazo.App/Package.appxmanifest` |
| Logos y mosaicos | `src/Trazo.App/Assets/Store/`, generados por `tools/make-icon.ps1` |
| Compilar x64 + ARM64 y unirlos en un `.msixbundle` | `tools/build-msix.ps1` |
| Probar el paquete en este equipo, sin firmar (modo de desarrollador) | `tools/test-msix.ps1` |
| Compilación automática en cada versión | trabajo `store` de `.github/workflows/release.yml` (se activa cuando existe la identidad) |
| Política de privacidad | <https://m4ur0-gatt1.github.io/trazo/privacidad.html> |
| Textos de la ficha | [ficha.md](ficha.md) |

## Pasos (los 1 a 4 los haces tú, una sola vez)

1. **Cuenta de desarrollador.** Entra en <https://storedeveloper.microsoft.com> y crea una cuenta **individual** con tu cuenta Microsoft. Pide verificar tu identidad (documento y selfi). Comprueba allí el coste vigente: para particulares no ha tenido coste en los últimos años.
2. **Reservar el nombre.** En el [Centro de partners](https://partner.microsoft.com/dashboard) › *Aplicaciones y juegos* › *Nueva aplicación* › reserva **Trazo**. Si ya está tomado, prueba con «Trazo Paint» o «Trazo — Estudio de pintura».
3. **Copiar la identidad.** En la aplicación reservada › *Administración de productos* › *Identidad del producto*, copia tres valores:
   - `Package/Identity/Name` (por ejemplo `12345MauroGatti.Trazo`)
   - `Package/Identity/Publisher` (`CN=…`)
   - `Package/Properties/PublisherDisplayName`
4. **Guardarla en GitHub.** En el repositorio › *Settings* › *Secrets and variables* › *Actions* › pestaña *Variables*, crea:
   - `TRAZO_STORE_IDENTITY` = Name
   - `TRAZO_STORE_PUBLISHER` = Publisher
   - `TRAZO_STORE_PUBLISHER_NAME` = PublisherDisplayName

   No son secretos: forman parte del paquete público.
5. **Paquete.** En la siguiente versión (`vX.Y.Z`), el flujo de publicación compila `Trazo-X.Y.Z.msixbundle` y lo deja como artefacto **store-msixbundle** de la ejecución. También se puede compilar a mano:
   ```
   powershell -ExecutionPolicy Bypass -File tools/build-msix.ps1 -Version 1.0.0 -IdentityName "…" -Publisher "CN=…" -PublisherDisplayName "…"
   ```
6. **Envío.** En el Centro de partners › *Inicio del envío*:
   - **Paquetes**: sube el `.msixbundle`.
   - **Listas de la Store**: usa [ficha.md](ficha.md) y capturas de 1920 × 1080 (mejor con obras reales que con la escena de la autoprueba).
   - **Clasificación por edad**: el cuestionario (no hay contenido generado por usuarios compartido, ni compras ni chat).
   - **Precio**: gratis.
   - **Propiedades**: categoría *Foto y vídeo* › *Ilustración y diseño gráfico*; política de privacidad: la URL de arriba.
   - **Declaración de `runFullTrust`**: «Trazo es una aplicación de escritorio WinUI 3 que necesita acceso completo para leer y escribir los archivos de dibujo que el usuario abre y guarda, y para usar la GPU con Direct2D.»
7. **Certificación.** Microsoft revisa el envío, normalmente en uno a tres días hábiles. Si lo rechaza, el informe dice qué corregir.

## Verificado y pendiente

- ✅ El paquete x64 se compila, se registra en Windows y pasa la autoprueba completa **dentro del contexto del paquete**; después se desinstala sin dejar nada (`tools/test-msix.ps1`).
- ✅ Se compila el paquete ARM64 y el lote x64 + ARM64.
- ⬜ No se ha pasado el Kit de certificación de aplicaciones de Windows (WACK): no está instalado en este equipo. La Store ejecuta sus propias comprobaciones al enviar.
- ⬜ El paquete ARM64 no se ha ejecutado en un equipo ARM.
