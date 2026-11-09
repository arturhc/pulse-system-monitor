PULSE / Monitor del sistema — Windows x64

VERSIÓN 1.2 — PULSE SYSTEM MONITOR
Código fuente, manual completo y actualizaciones:
https://github.com/arturhc/pulse-system-monitor

Esta versión detecta el equipo y los núcleos en tiempo de ejecución.
No incluye reportes, métricas ni instaladores externos en la distribución.
Si falta PawnIO, el botón de sensores avanzados descarga la versión
oficial 2.2.0 y verifica su SHA-256 antes de pedir permiso a Windows.
Solo esa descarga opcional requiere internet. La operación es local.

BATERÍA, HISTORIALES Y DIAGNÓSTICOS
La tarjeta de batería distingue la carga actual de la salud/capacidad.
Salud = capacidad de carga completa / capacidad de diseño. El desgaste
es 100 - salud. Estas estimaciones del controlador varían con calibración
y temperatura; no representan una prueba de autonomía. Pulse marca
menos de 80 % como desgaste y menos de 60 % como desgaste alto; son
etiquetas orientativas de la aplicación. Los ciclos se muestran solo
cuando el equipo los informa.

Historial: muestras cada 15-16 segundos mientras Pulse esté abierto,
guardadas en history.sqlite. Se conservan 30 días de métricas y un año
de salud diaria de batería. No se reconstruyen datos anteriores a la
instalación ni periodos con Pulse cerrado. Pestaña Historial: gráfica
por periodo, evolución diaria de capacidad y exportación CSV de muestras.

Diagnósticos: reporte de batería de Windows, capacidades/ciclos,
memoria y espacio disponibles, estado WMI de almacenamiento, SMART si
se expone y eventos seleccionados de hardware de los últimos 7 días.
No reemplaza pruebas de memoria, SMART extendido o diagnóstico técnico.
No ejecuta pruebas de estrés, reparaciones ni reinicios.
Cada ejecución se guarda en diagnosticos/FECHA/ con HTML y JSON y los
reportes de batería nativos. Los reportes se conservan hasta que el
usuario los elimine. Pueden consultarse desde Diagnósticos.
La base de historial usa SQLite de Windows (winsqlite3.dll).

Abrir Pulse.exe. La ventana funciona localmente y no necesita internet.
Instalar Pulse.exe copia la app a %LOCALAPPDATA%\Programs\PulseWidget,
crea accesos directos en Inicio y Escritorio, y abre el widget.

LECTURAS
CPU: porcentaje global del sistema, medido con GetSystemTimes.
RAM: memoria física ocupada/total, medida con GlobalMemoryStatusEx.
SSD: actividad y transferencia de todos los discos físicos; espacio de C:.
GPU: utilización del motor más ocupado, agregada por motor entre procesos.
Batería: porcentaje, conexión eléctrica y autonomía si Windows la informa.
Temperaturas: sensores accesibles mediante LibreHardwareMonitor. No se
inventan valores ni se interpreta un dato faltante como cero.

CPU/RAM/GPU/actividad: cada 2 segundos. Sensores de almacenamiento: cada
30 segundos. La lectura de un sensor de disco puede despertar esa unidad.
El widget tiene un historial de hasta 2 minutos para CPU y GPU en memoria.

CONTROLES
Arrastrar la barra superior: mover. Esquina inferior derecha: redimensionar.
Primer botón superior: fijar/desfijar encima de otras ventanas.
Menos: ocultar junto al reloj. Doble clic en el icono: volver a mostrar.
X: salir y detener el monitoreo.
Ampliar: detalles, frecuencias y todas las temperaturas detectadas.
Engrane: inicio con Windows y sensores avanzados.

SENSORES AVANZADOS
Algunos sensores necesitan permisos de administrador. El botón reinicia
Pulse y muestra la confirmación de Windows. Si la rechazas, Pulse continúa
en modo estándar. Incluso con permisos puede haber sensores no disponibles.
Al iniciar con Windows se usa el modo estándar; no se omite la confirmación
de administrador ni se instala un servicio de monitoreo permanente.
Los sensores internos de CPU utilizan el controlador oficial firmado
PawnIO (https://pawnio.eu/). Si no está instalado, algunas temperaturas
no estarán disponibles. No desactives protecciones de Windows para leer
un sensor que el sistema bloquee.

DATOS LOCALES
Preferencias, caché de WebView2 y una última lectura se guardan en
%LOCALAPPDATA%\PulseWidget. No hay cuenta, nube ni telemetría remota.
--diagnose guarda una muestra en diagnostics.json y finaliza sin ventana.

TECNOLOGÍA Y FUENTES
.NET 10, Windows Forms y Microsoft Edge WebView2.
https://learn.microsoft.com/microsoft-edge/webview2/
LibreHardwareMonitorLib 0.9.6, licencia MPL-2.0:
https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LICENSE
Microsoft.Web.WebView2:
https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35
Las dependencias mantienen sus respectivas licencias. El código fuente
de Pulse se entrega por separado para reproducir o modificar la app.

COMPILAR DESDE EL CÓDIGO
Con SDK .NET 10 para Windows:
dotnet publish Pulse.csproj -c Release -r win-x64 --self-contained true

Este programa solo consulta hardware. No modifica planes de energía,
ventiladores, frecuencias, voltajes ni protecciones del sistema.
