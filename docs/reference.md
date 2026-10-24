# Paso 1 — Definición de la referencia

Fecha: 2026-10-24.

## Producto de referencia

| Campo | Valor | Fuente |
|---|---|---|
| Aplicación | Procreate para iPad | — |
| Versión del manual | Procreate Handbook **5.4** | [Brush Studio Settings](https://help.procreate.com/procreate/handbook/brushes/brush-studio-settings) |
| Versión vigente de la app | **5.4.14** (iPadOS 16.3 o posterior), según la FAQ oficial el día de la consulta | [Procreate FAQ](https://help.procreate.com/articles/dbgjal-procreate-faq) |
| Cambios de 5.4 relevantes | Nuevo sistema de bibliotecas de pinceles y cambios en Stroke Path (Spacing Jitter, Jitter lateral y lineal), entre otros | [What's new in 5.4](https://help.procreate.com/articles/Ls9oMu-procreate-5-4-update-at-a-glance) |

La versión menor cambia a menudo: una copia en caché de la misma FAQ mostraba 5.4.11. **Cada observación experimental debe registrar la versión exacta instalada en el iPad.**

## Tipos de evidencia

| Nivel | Significado | Uso |
|---|---|---|
| **Documentado** | Lo describe el fabricante en el manual o en notas oficiales | Base para el inventario funcional; no prueba cómo se comporta en la práctica |
| **Observado** | Medido en un iPad con una versión concreta, siguiendo un protocolo | Necesario para fijar rangos, curvas y fórmulas |
| **Hipótesis** | Inferencia técnica sin verificar | Solo para diseñar experimentos; nunca como requisito |

Advertencia sobre el inventario actual: se elaboró con una herramienta que **resume** las páginas en lugar de devolver el texto literal. Antes de convertir una fila en requisito normativo hay que cotejarla con la página original.

## Dispositivos

| Rol | Dispositivo | Estado |
|---|---|---|
| Observación de Procreate | iPad con Apple Pencil (2.ª generación o Pro), iPadOS ≥ 16.3 | **Pendiente**: hace falta acceso a un iPad para todas las mediciones "Observado" |
| Objetivo de Trazo — lápiz | Surface Pro con Surface Pen (Windows Ink), tableta Wacom | **Pendiente**: el lápiz real no se ha probado; las pruebas usan entrada sintética y grabaciones |
| Objetivo de Trazo — táctil | Portátil 2 en 1 con pantalla multitáctil | Pendiente de prueba física |
| CI y referencias | Direct3D WARP (renderizador por software), Windows Server en GitHub Actions | Operativo |

## Alcance

**Incluido en esta fase (núcleo de pintura):** entrada del lápiz, trayectoria, motor de pinceles, capas, composición, historial y persistencia (BR-01/02, LY-01, CO-01, HI-01, FI-01, PF-01).

**Fuera de esta fase:** animación (AN-01), perfiles de color y CMYK, interoperabilidad con PSD y con el formato nativo `.procreate` (EX-01).

Sobre `.procreate`: que el manual describa que conserva capas y máscaras **no demuestra** que una aplicación independiente pueda leerlo y escribirlo sin pérdidas. Se investigará aparte, revisando también sus implicaciones legales.
