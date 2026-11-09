# Seguridad

Se mantiene la versión pública más reciente. Si encuentras una vulnerabilidad, usa [el reporte privado de GitHub](https://github.com/arturhc/pulse-system-monitor/security/advisories/new). No publiques credenciales, reportes de batería, bases del historial o identificadores de dispositivos en una incidencia pública.

## Límites del diseño

- El frontend carga archivos locales y solo puede solicitar comandos explícitos del host. No tiene un evaluador genérico de shell.
- Navegación externa, ventanas nuevas y permisos WebView2 se bloquean; la CSP impide conexiones del frontend.
- Los sensores avanzados pueden elevar la aplicación completa. La superficie web sigue siendo exclusivamente local y confiable; modifica únicamente archivos que controles.
- PawnIO es opcional. La descarga usa una URL de release fijada y un SHA-256 conocido; la instalación requiere la confirmación de Windows. Pulse no desactiva defensas ni instala la edición sin restricciones.
- Los datos locales no están cifrados por la app y pueden ser leídos por programas con permisos sobre el mismo perfil.
- Los binarios de Pulse no están firmados con un certificado propio. Revisa fuentes y procedencia antes de ejecutarlos.

Consulta [datos y privacidad](docs/DATA-AND-PRIVACY.md) para saber qué información puede contener un reporte antes de compartirlo.
