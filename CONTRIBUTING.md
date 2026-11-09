# Contribuir a Pulse

Antes de proponer un cambio, revisa [arquitectura](docs/ARCHITECTURE.md) y [desarrollo](docs/DEVELOPMENT.md). Abre una incidencia que describa el problema, el comportamiento esperado y el sistema/arquitectura. Indica qué sensor falta, sin adjuntar reportes completos que contengan identificadores privados.

Para un pull request:

1. Crea una rama para un cambio concreto.
2. Conserva datos desconocidos como nulos; no uses cero para esconder errores de sensores.
3. Añade una prueba cuando cambien cálculos, persistencia o reglas de interpretación.
4. Ejecuta `scripts/build.ps1` y `scripts/test.ps1 -SkipBuild`.
5. Actualiza la documentación si cambian permisos, almacenamiento, controles o significado de las métricas.

Las migraciones deben conservar datos válidos y explicar las pérdidas inevitables. No incluyas métricas reales en fixtures, capturas, commits o archivos de prueba. Las dependencias nuevas deben tener una función clara y sus avisos correspondientes.

La UI y los mensajes actuales están en español. Se aceptan mejoras de accesibilidad y documentación; un sistema de idiomas completo requeriría una propuesta separada.
