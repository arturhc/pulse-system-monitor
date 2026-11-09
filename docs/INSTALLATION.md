# Instalación y operación

## Requisitos y distribución

- Windows 11 x64 es la plataforma validada.
- WebView2 Runtime de Microsoft: [descarga oficial](https://developer.microsoft.com/microsoft-edge/webview2/).
- La distribución autocontenida incluye el runtime .NET requerido; no incluye WebView2 ni el instalador de PawnIO.
- No hay soporte validado para Linux, macOS, Windows ARM64 o Windows x86.

Extrae todo el ZIP. `Pulse.exe` ejecuta el monitor desde esa carpeta. `Instalar Pulse.exe` copia los archivos a `%LOCALAPPDATA%\Programs\PulseWidget`, crea accesos `Pulse` en Inicio y Escritorio y abre la app. La instalación normal es por usuario y no registra un servicio de monitoreo.

## Actualizar

Extrae la nueva versión en otra carpeta y ejecuta su instalador. El instalador cierra la instancia instalada antes de reemplazar archivos. Si esa instancia está elevada, ejecuta el instalador como administrador y acepta la confirmación de Windows. Los datos están fuera de la carpeta del programa y se conservan.

Las migraciones son aditivas o conservan datos desconocidos como nulos. La migración de esquema 1 a 2 elimina las temperaturas históricas de SSD cuyo máximo podía incluir umbrales; no intenta reconstruir una medición que no existe.

## Sensores avanzados

CPU/RAM/actividad/GPU y datos básicos de batería suelen funcionar sin elevación. Los sensores internos pueden requerir PawnIO y permisos de administrador.

Al pulsar **⚙ → Activar sensores avanzados**:

1. Pulse comprueba si existe el servicio PawnIO.
2. Si falta, obtiene la versión oficial 2.2.0, comprueba el hash fijado y pide autorización para instalar la edición firmada.
3. Reinicia Pulse con elevación y espera a que termine la instancia anterior.

Si cancelas UAC, el modo estándar continúa. El inicio con Windows permanece en modo estándar. Que exista un controlador no garantiza que todos los sensores sean accesibles. La GPU integrada puede no exponer una temperatura independiente; RAM y batería también pueden carecer de ese sensor.

## Solución de problemas

| Síntoma | Qué revisar |
| --- | --- |
| No abre la interfaz | Extrae todo el ZIP y comprueba WebView2 Runtime. Revisa `errors.log` en la carpeta de datos. |
| Temperatura «Sin sensor» | Activa sensores avanzados; si continúa, el proveedor puede no exponerla. No desactives protecciones del sistema. |
| No se completa la descarga de PawnIO | Comprueba conexión al origen oficial y vuelve a intentar. Una discrepancia de hash detiene la instalación. Un archivo de caché dañado puede retirarse manualmente de `downloads/`, con Pulse cerrado. |
| Historial vacío | El registro empieza al abrir Pulse. Elige un periodo que contenga muestras; los datos anteriores no se inventan. |
| «Sin actualizar» | La interfaz lleva más de ocho segundos sin recibir una muestra. Revisa el proceso o reinicia Pulse; no es un valor de uso de CPU. |
| La actualización indica archivo en uso | Cierra Pulse. Si está elevado, utiliza un instalador elevado. |
| Batería sin ciclos/autonomía | El controlador puede no informar esos campos. No se sustituye un valor desconocido por cero. |

## Desinstalar

1. Sal de Pulse con **×** o desde el menú de la bandeja.
2. Desactiva «Iniciar con Windows» antes de quitar el programa, si estaba activo.
3. Retira la carpeta del programa y sus accesos directos.
4. Opcionalmente, conserva o elimina `%LOCALAPPDATA%\PulseWidget`, donde están los reportes e historiales.

PawnIO es un componente separado que otras apps pueden utilizar. Su desinstalación se gestiona con las opciones de Windows o su instalador oficial, no eliminando archivos del controlador manualmente.
