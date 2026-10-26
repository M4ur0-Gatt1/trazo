# Historial de versiones

Las notas de cada versión publicada están en [Releases](https://github.com/M4ur0-Gatt1/trazo/releases). El detalle técnico y las pruebas de cada cambio están en [docs/results](docs/results/).

## 1.2.0 — 2026-10-26

- **PSD (EX-01):** exportar e importar con capas, grupos, máscaras, recorte, bloqueo alfa y los 16 modos de fusión. Lee RGB y escala de grises de 8 y 16 bits; avisa de lo que no puede conservar igual (modos sin equivalente, grupos anidados, capas de ajuste). Verificado contra ag-psd, la biblioteca de Photopea.
- **Interfaz en inglés (I18N-01):** español o inglés según el idioma de Windows, o elegido en Acciones › Preferencias › Idioma. Los porcentajes siguen la tipografía de cada idioma y los números, el formato regional de Windows.
- En Licuar, el deslizador «Ajustar» pasa a llamarse «Cantidad», y el botón de difuminar la selección, «Difuminar borde», para no confundirlo con la herramienta.

## 1.1.0 — 2026-10-25

- **Corrección:** «Añadir texto» y la etiqueta de QuickShape fallaban en la versión instalada con el `.exe` (Win2D no resuelve `ms-appx:///` sin paquete).
- **Recuperación tras fallos (RC-01):** aviso al volver a abrir tras un cierre inesperado, recuperación de la obra si se reinicia la tarjeta gráfica y copia de emergencia «(recuperado)» ante un error inesperado.
- **Rendimiento (PF-01):** el autoguardado solo codifica las capas que cambiaron (de 9,4 s a 0,5 s con 20 capas de 4K); la vista reutiliza la composición de las capas que no cambian (de 25 a 6 ms por fotograma, p95); vista previa de Deformar 6 veces más rápida (de 87 a 14 ms).
- **Acerca de Trazo** con versión, licencias y avisos de terceros, que ahora se distribuyen con la app.
- Repositorio: política de seguridad, guía para contribuir, plantillas de *issues* y *pull requests*, y actualizaciones de dependencias con Dependabot.

## 1.0.0 — 2026-10-25

Primera versión estable.

- **Curvas de presión (BR-06)** para el tamaño, el flujo y la opacidad, con editor en Ajustes del pincel.
- **Opacidad por presión separada del flujo (BR-06):** un tope por trazo que no sube al superponer sellos. Se calcula en la CPU porque Direct2D no tiene una mezcla «máximo» fiable.
- **Mezcla húmeda (BR-04):** dilución, carga, ataque y arrastre del color de la capa; activada en el gouache, el óleo y la acuarela.

## 0.2.0-beta.5 — 2026-10-25

- Transformar en cuatro modos: libre, uniforme, distorsionar y deformar, con modo magnético (TR-01).
- Licuar: empujar, girar, pellizcar, expandir y reconstruir (LQ-01).
- Guías de dibujo (cuadrícula 2D, isométrica y perspectiva) con Asistente de dibujo (DG-01).

## 0.2.0-beta.4 — 2026-10-25

- Selección automática, a mano, rectángulo y elipse (SE-01).
- Máscaras de capa y grupos de capas (LY-02, LY-03). Formato `.trazo` versión 3.

## 0.2.0-beta.3 — 2026-10-24

- Corrección: el lienzo podía dejar de redibujarse al alejar mucho el zoom.
- Nuevo icono e identidad visual.

## 0.2.0-beta.2 — 2026-10-24

- Grano de papel en movimiento y texturizado (BR-03) y afinado de extremos (BR-05).
- Guardado verificado con copia de seguridad (FI-01).

## 0.2.0-beta.1 — 2026-10-24

- Primera beta de la app nativa (WinUI 3 y Win2D) con instalador.
