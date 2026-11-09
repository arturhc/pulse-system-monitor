# Componentes de terceros

La licencia MIT de este repositorio cubre el código original de Pulse. Los componentes de terceros conservan sus propias licencias; NuGet resuelve sus dependencias transitivas en `packages.lock.json`.

| Componente | Uso | Origen / licencia |
| --- | --- | --- |
| .NET 10 | Ejecución, Windows Forms y APIs del sistema | [dotnet](https://github.com/dotnet), licencias y avisos incluidos en el runtime |
| Microsoft.Web.WebView2 | Interfaz HTML/CSS/JS dentro de Windows | [paquete y licencia](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35) |
| LibreHardwareMonitorLib | Sensores, batería y SMART NVMe | [fuente](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), MPL-2.0 y avisos propios; copias en `vendor/` |
| Microsoft.Data.Sqlite.Core | Persistencia local | [fuente](https://github.com/dotnet/efcore), MIT |
| SQLitePCLRaw | Adaptador de SQLite de Windows | [fuente](https://github.com/ericsink/SQLitePCL.raw), Apache-2.0 |
| winsqlite3.dll | Motor SQLite | Componente de Windows; no se distribuye una biblioteca SQLite nativa adicional |
| PawnIO | Acceso opcional a sensores internos | [sitio oficial](https://pawnio.eu/), [fuente y condiciones](https://github.com/namazso/PawnIO); copia del aviso en `vendor/PawnIO-LICENSE.txt` |

## PawnIO es opcional

El instalador de PawnIO **no está incluido en Git ni en el ZIP público**. Si el usuario pulsa «Activar sensores avanzados» y el controlador no está instalado, Pulse descarga la versión oficial 2.2.0, verifica su SHA-256 y pide la autorización de Windows. No instala la edición sin restricciones y no modifica protecciones de Windows.

- Origen: `https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe`
- SHA-256: `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`
- Versión fijada intencionalmente. Una actualización debe revisar el origen, la firma del archivo y el hash antes de cambiar el código.

No se modificó LibreHardwareMonitor ni se incorpora su código fuente en los archivos originales de Pulse. Sus fuentes y las de sus dependencias están disponibles en los proyectos y paquetes indicados arriba.
