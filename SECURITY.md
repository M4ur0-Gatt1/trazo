# Seguridad

## Versiones con soporte

| Versión | Soporte |
|---|---|
| 1.x (la más reciente) | ✅ Correcciones de seguridad |
| 0.2.0-beta.* | ❌ Actualiza a la versión estable |

## Cómo informar de una vulnerabilidad

**No abras un *issue* público.** Usa el aviso privado de GitHub: pestaña [Security](https://github.com/M4ur0-Gatt1/trazo/security) › **Report a vulnerability**. Solo el responsable del proyecto ve el informe.

Incluye, si puedes:

- versión de Trazo (*Acciones › Acerca de Trazo*) y de Windows;
- pasos para reproducirlo y, si se trata de un archivo `.trazo` malformado, el archivo;
- qué impacto crees que tiene.

Recibirás una primera respuesta en un plazo de 7 días. Cuando haya corrección, se publicará una versión nueva y se reconocerá tu aporte si lo deseas.

## Superficie de ataque

Trazo no se conecta a internet ni ejecuta código de terceros. El riesgo principal está en los archivos que abre: proyectos `.trazo` (ZIP con un manifiesto JSON e imágenes PNG) e imágenes importadas. El lector de `.trazo` valida las dimensiones, el tamaño de cada capa y la estructura antes de usar los datos, y rechaza versiones del formato que no conoce ([FI-01](docs/protocols/FI-01.md)).
