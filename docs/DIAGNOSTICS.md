# Qué revisa el diagnóstico

Cada ejecución guarda una carpeta con marca de tiempo. Incluye `diagnostico.html`, `diagnostico.json` y, cuando Windows permite generarlos, `bateria-windows.html` y `bateria-windows.xml`.

## Comprobaciones

| Comprobación | Fuente | Interpretación |
| --- | --- | --- |
| Batería | `powercfg /batteryreport`, sensores y WMI | Capacidad de diseño/completa, salud, desgaste y ciclos informados |
| Memoria | Muestra de memoria física | Uso/disponibilidad; no detecta celdas defectuosas |
| Espacio | Unidad del sistema | Espacio libre y porcentaje; menos de 10 % se marca para atención |
| Estado de disco | `Win32_DiskDrive` | Estado general expuesto por Windows; un `OK` no es una prueba completa |
| Predicción SMART | `MSStorageDriver_FailurePredictStatus` | Puede no existir para NVMe o no estar accesible; «no disponible» no significa fallo |
| Resistencia/reserva NVMe | Sensores de LibreHardwareMonitor | Resistencia restante/consumida y reserva, si el controlador las expone; no predice todos los fallos |
| Temperaturas | Muestra de sensores | Temperaturas actuales disponibles; nunca umbrales de advertencia/críticos ni distancia a TjMax |
| Energía | `powercfg /getactivescheme` | Identifica el plan; no modifica configuraciones |
| Eventos | Registro `System`, últimos siete días | Categorías seleccionadas, hasta los 100 eventos más recientes |

Eventos consultados: Kernel-Power 41; WHEA-Logger; proveedor `disk`, IDs 7, 51 y 153; proveedor `stornvme`, ID 129. No se revisan todos los eventos de Windows y un evento no identifica por sí solo la causa de un problema.

## Estados

- **completado / informado / consultado:** hay un resultado para esa consulta. No equivale a certificar todo el hardware.
- **atención / revisar:** se alcanzó un criterio de la app o existen eventos que merece la pena revisar.
- **parcial / no disponible:** faltan datos, el proveedor no los expone o no hay permisos suficientes.

## Alcance

No ejecuta benchmarks, descargas completas de batería, pruebas extensas SMART, reparación de archivos, reinicios ni pruebas físicas de memoria. No cambia ventiladores, voltajes, frecuencias, planes de energía ni protecciones de Windows. Las temperaturas son una muestra puntual; no sustituyen una prueba térmica sostenida.

Las consultas externas `powercfg` tienen tiempo límite. El monitoreo continúa mientras se generan reportes. La revisión inicial se realiza una vez por versión; las ejecuciones adicionales se solicitan desde **Diagnósticos**.

Para interpretar la batería, distingue carga, salud, desgaste y ciclos. Un contador bajo de ciclos no explica por sí solo una pérdida de capacidad; también intervienen edad, calibración, temperatura y cómo informe el firmware. Pulse presenta las mediciones, no diagnostica por sí solo una causa química o eléctrica.
