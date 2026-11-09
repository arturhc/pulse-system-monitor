# Desarrollo y publicación

## Entorno

- Windows 11 x64 y PowerShell 7.2+.
- SDK .NET fijado en `global.json` (10.0.403; se permiten parches compatibles).
- Node.js 22+ para las pruebas del frontend; no hay dependencias npm.
- WebView2 Runtime para abrir la interfaz. No hace falta para las pruebas de almacenamiento.

Las referencias NuGet y su grafo transitivo se guardan en `packages.lock.json`. Para actualizar dependencias intencionalmente, cambia las referencias, ejecuta `dotnet restore Pulse.csproj -r win-x64 --force-evaluate`, revisa el diff del lock y vuelve a correr las pruebas. Actualiza SDK, lock y CI de forma conjunta cuando una actualización de runtime lo requiera.

## Comandos

```powershell
./scripts/build.ps1
./scripts/test.ps1 -SkipBuild
./scripts/package.ps1 -SkipBuild
```

Compilación manual equivalente:

```powershell
dotnet restore Pulse.csproj -r win-x64 --locked-mode
dotnet publish Pulse.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts/win-x64 -p:DebugType=None -p:DebugSymbols=false
node --test tests/display.test.cjs
```

El alias `Instalar Pulse.exe`, la documentación y los avisos adicionales se preparan con el script de build. Publica a una carpeta limpia si cambias el conjunto de dependencias para evitar archivos antiguos que ya no son necesarios.

## Qué se prueba

Las pruebas JavaScript usan `node:test` y un DOM reducido, sin ejecutar un navegador ni simular que los fixtures son lecturas reales del monitor. Cubren datos ausentes, memoria/unidades, escape de contenido, frescura, exclusión de umbrales térmicos y detección de hardware sin constantes de una laptop.

`StorageTests.cs`, invocado con `--test-storage <carpeta>`, trabaja en una base aislada bajo `artifacts/tests/`. Comprueba salud/desgaste de batería, valores desconocidos, límite de muestreo, persistencia tras reapertura, retención de 30/365 días, actualización diaria, CSV y temperaturas nulas. No instala controladores, no enumera hardware y no corre diagnósticos reales.

La plantilla `docs/ci/windows-ci.yml` está preparada para ejecutar compilación, ambas familias de pruebas y empaquetado. Las acciones están fijadas a commits. El job solo necesita permiso de lectura del repositorio y publica el ZIP como artifact del workflow. No está activada en esta publicación; la validación de la release se ejecutó localmente.

Las pruebas automáticas no validan todos los modelos de CPU/GPU/SSD. Para QA manual comprueba modo estándar/elevado, cancelación de UAC, cierre y reinicio, pin/bandeja, DPI, distintos periodos del historial y generación de reportes. Nunca uses datos personales para capturas públicas.

## CLI de la app

| Opción | Acción |
| --- | --- |
| `--install` | Instalar por usuario y abrir |
| `--quit` | Solicitar cierre de la instancia instalada con el mismo nivel de permisos |
| `--diagnose` | Guardar una muestra de telemetría en `diagnostics.json` y salir |
| `--run-diagnostics` | Capturar métricas, registrar una muestra y generar un diagnóstico real |
| `--export-history` | Exportar todas las muestras retenidas a la carpeta local de exportaciones |
| `--test-storage <carpeta>` | Ejecutar pruebas aisladas; retorna 1 si fallan |

`--elevated` es un detalle interno del reinicio con UAC, no un método para omitir permisos.

## Crear una release

1. Ajusta `<Version>` y `CHANGELOG.md`.
2. Restaura con lock, compila y prueba en Windows.
3. Genera el ZIP y revisa su contenido: sin historiales, reportes, bases, credenciales ni instaladores externos.
4. Comprueba los resultados locales para el commit exacto. Si activaste CI, exige también su resultado correcto.
5. Crea un tag `vX.Y.Z` y publica el ZIP y `.sha256` con GitHub CLI o desde Releases.

```powershell
gh release create vX.Y.Z artifacts/packages/Pulse-System-Monitor-vX.Y.Z-win-x64.zip artifacts/packages/Pulse-System-Monitor-vX.Y.Z-win-x64.zip.sha256 --title 'Pulse System Monitor X.Y.Z' --notes-file release-notes.md
```

No se generan firmas de código propias ni actualizaciones automáticas. El ZIP se verifica con su hash, pero un hash no reemplaza una firma de un editor de confianza.

## Activar GitHub Actions

La credencial OAuth usada para la publicación inicial permite crear y subir el repositorio, pero no crear workflows. La plantilla se mantiene fuera de `.github/workflows/` para no exigir un permiso adicional al publicar el código.

Cuando el propietario decida habilitar CI:

```powershell
gh auth refresh --hostname github.com --scopes workflow
# Completar personalmente la autorización que muestra GitHub.
New-Item -ItemType Directory -Force .github/workflows
Copy-Item docs/ci/windows-ci.yml .github/workflows/ci.yml
git add .github/workflows/ci.yml
git commit -m 'Enable Windows CI'
git push
```

El archivo copiado utiliza únicamente las pruebas aisladas descritas arriba, sin instalar controladores ni sondear el hardware del runner. Puedes reproducir la captura de demostración con Python 3: `python scripts/preview-demo.py`, luego abre `http://127.0.0.1:8765`. Ese servidor solo sirve fixtures sintéticos y recursos de la UI.
