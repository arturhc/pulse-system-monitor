# Datos, historial y privacidad

## Ubicación

La app escribe en `%LOCALAPPDATA%\PulseWidget`:

| Archivo o carpeta | Contenido |
| --- | --- |
| `preferences.json` | Posición, vista ampliada y pin |
| `history.sqlite`, `-wal`, `-shm` | Historial SQLite en modo WAL |
| `latest.json` | Última muestra, sobrescrita aproximadamente cada 30 s |
| `diagnosticos/<fecha>/` | Cada ejecución de diagnóstico, con HTML, JSON e informes nativos de batería |
| `last-diagnostic.json` | Último resultado para volver a mostrarlo |
| `diagnostic-version.txt` | Versión que ya corrió la revisión inicial |
| `exportaciones/` | CSV solicitados por el usuario |
| `downloads/` | Caché del instalador opcional fijado de PawnIO |
| `WebView/` | Perfil/caché de WebView2 |
| `errors.log` | Errores locales de la app |

La opción de inicio con Windows crea o elimina `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PulseWidget`. Los binarios instalados se guardan en una carpeta distinta: `%LOCALAPPDATA%\Programs\PulseWidget`.

## Qué se registra

`samples` guarda una fila aproximadamente cada 15–16 segundos mientras Pulse está abierto. Campos:

```text
ts (Unix ms, clave primaria)
cpu, ram, gpu, disk (porcentajes)
read_bps, write_bps (bytes/s)
battery, plugged, health
cpu_temp, gpu_temp, ssd_temp (°C o NULL)
```

`battery_daily` conserva la última estimación válida del día local:

```text
day (AAAA-MM-DD, clave primaria), ts
health, design_wh, full_wh, cycles
```

Los valores ausentes permanecen en `NULL`; en CSV se representan como una celda vacía. La fecha de exportación lleva zona horaria y los números usan punto decimal para facilitar su análisis.

## Retención y agregación

- Métricas: **30 días**.
- Salud diaria de batería: **365 días**.
- La limpieza se realiza aproximadamente cada hora mientras hay muestras nuevas; no se ejecuta una tarea externa cuando Pulse está cerrado.
- Los reportes y CSV solicitados se conservan hasta que el usuario los elimine.
- Las gráficas agrupan los puntos según el periodo. CPU/RAM/GPU/disco/batería usan promedios de intervalo; temperaturas usan máximos del intervalo. El texto del gráfico distingue el máximo de esos puntos de un pico instantáneo no registrado.
- Los huecos de registro no se interpolan como si fueran lecturas reales.

Solo hay una muestra diaria de salud en el registro anual, no cada oscilación del controlador. La tabla de la UI muestra hasta los últimos 30 días registrados; la base conserva el año completo.

## Privacidad y red

Pulse no envía métricas a un servidor, no requiere cuenta y no tiene analítica remota propia. El frontend no realiza conexiones de red. La excepción funcional es la descarga opcional de PawnIO desde su release oficial de GitHub, cuando el usuario pulsa el botón correspondiente y el controlador falta. Esa conexión tiene las implicaciones normales de una descarga HTTPS.

Los informes nativos de Windows pueden contener nombre del equipo, modelos, números de serie o identificadores de batería y patrones de uso. La última muestra también puede contener identificadores de dispositivos. **Revisa los archivos antes de compartirlos en una incidencia o publicarlos.** El repositorio y las releases no contienen esos archivos.

La base no está cifrada por Pulse: está protegida por los permisos del perfil de Windows. Ejecutar el programa elevado amplía sus permisos locales. No proporciona aislamiento frente a otros programas que puedan leer el mismo perfil.

## Respaldo

Cierra Pulse y copia la carpeta de datos. Si necesitas copiar mientras está abierto, utiliza una herramienta de respaldo de SQLite que respete WAL; copiar solamente `history.sqlite` durante una escritura puede omitir transacciones recientes. Para compartir métricas numéricas, usa la exportación CSV y revísala primero.
