# SE_HardwareInserter

Solid Edge add-in that inserts fasteners and anchors (`.par` files) into the active assembly,
capturing the diameter of a selected hole and automatically applying the matching geometric
constraints.

*(Español más abajo / Spanish below)*

## Installation (end user)

1. Download `SE_HardwareInserterSetup-<version>.exe` from the [Releases page](../../releases) of this
   repository, or copy it from a USB drive — it's a single file, with no external dependencies
   and no internet connection required during installation.
2. Double-click to run it. No administrator rights required (it installs under your user profile
   and registers only for your user).
   - Official release assets must carry a valid Authenticode signature. Verify the publisher and
     signature before running them; local developer builds are unsigned.
3. The installer will stop if Solid Edge is open. Close it without forcing termination, install,
   then open Solid Edge 2022-2026 with an assembly **saved to disk**.
4. A **"Insert Hardware"** button should appear on the ribbon of the assembly environment, under
   the "Hardware" category. If it doesn't, see Troubleshooting below.

### Requirements

- Windows with Solid Edge 64-bit 2022-2026 installed.
- .NET Framework 4.8 (preinstalled on up-to-date Windows 10/11).

## First use

1. Click "Insert Hardware" to open the panel.
2. Click **"Folder..."** and select the root folder containing your `.par` fastener files (they
   can be organized into nested subfolders, reproduced recursively in the tree). This path is remembered for next
   time.
3. In Solid Edge, Ctrl+click to select the inner cylindrical face of the hole where you want to
   insert the fastener (and optionally its flat support face).
4. Click **"Capture Hole"** — the detected diameter should be displayed. Verify the value looks
   reasonable (this is the first real-world validation of the add-in's unit conversion).
5. Select a `.par` file from the tree and click **"Insert"** (or double-click). If the "Apply
   constraints" checkbox is checked, the add-in first tries automatic placement and then keeps the
   grounded occurrence for guided face selection when needed. You can apply the selected faces,
   keep it free, or cancel; closing the panel rolls back any pending occurrence. Unchecked, the file
   is copied and SHA-256 verified asynchronously before COM insertion and is inserted freely.

## Troubleshooting

- **Button doesn't appear on the ribbon**: check that COM registration succeeded — open
  `regedit` and look under `HKEY_CURRENT_USER\Software\Solid Edge\Add-Ins`; the subkey
  `{DC87125A-6DBF-43A5-968B-2578CCBFC158}` should exist. If it doesn't, reinstall or run
  `dev/register-addin.ps1` manually from source.
- **"Save the assembly before inserting hardware"**: the add-in needs the active assembly to
  already have a path on disk (save it first with Ctrl+S).
- **Captured diameter looks wrong**: please report it — the unit conversion (meters→mm) isn't yet
  validated against every document configuration.
- **Diagnostics**: daily logs are stored in `%LocalAppData%\SE_HardwareInserter\logs`, rotate at
  5 MB and are retained for 14 days.

## Development

To build from source, run tests, or understand the project's architecture (a COM add-in, not a
standalone app), see [`AGENTS.md`](AGENTS.md).
The per-user COM keys, RegAsm golden and safe migration boundary are documented in
[`docs/COM-registration.md`](docs/COM-registration.md).

To build the installer locally (Windows, with [Inno Setup 6.7.3+](https://jrsoftware.org/isinfo.php)
installed):

```powershell
.\dev\check-prerequisites.ps1 -Profile Packaging
dotnet restore SE_HardwareInserter.slnx --locked-mode
dotnet build src\SE_HardwareInserter.AddIn\SE_HardwareInserter.AddIn.csproj -c Release -p:Platform=x64 --no-restore
ISCC.exe /DMyAppVersion=1.0.0 installer\SE_HardwareInserter.iss
# -> installer\Output\SE_HardwareInserterSetup-1.0.0.exe
.\dev\smoke-install.ps1 -InstallerPath .\installer\Output\SE_HardwareInserterSetup-1.0.0.exe
.\dev\smoke-uninstall.ps1
```

The four `packages.lock.json` files are committed, and normal builds use locked restore. Run an
unlocked restore only when intentionally updating dependencies, then review every lock-file diff.

Pull requests and pushes to `main` must pass the Windows x64 build/test CI and the independent
CodeQL C# analysis. Dependabot proposes weekly NuGet and GitHub Actions updates against `main`.
Signed production publishing remains blocked until Azure Artifact Signing has a validated account,
Public Trust profile and OIDC federation; signing credentials must never be stored in the repository.
It is also blocked until real `SE2022.json` through `SE2026.json` evidence exists. Follow
[`validation/solid-edge/README.md`](validation/solid-edge/README.md): validate one source commit,
then add all five results in a single evidence-only commit and tag that commit.
CI supplies the unsigned acceptance candidate and `source-commit.txt`; the final release rebuilds
locked dependencies, signs a different binary hash and validates it with the installer smoke.

Pushing a `vX.Y.Z` tag triggers the `.github/workflows/release.yml` workflow, which builds,
generates the installer, and publishes it as a GitHub Release asset.

## License

[MIT](LICENSE)

---

# SE_HardwareInserter (Español)

Add-in para Solid Edge que inserta tornillería y anchors (archivos `.par`) en un ensamblaje activo,
capturando el diámetro de un agujero seleccionado y aplicando las restricciones geométricas
correspondientes automáticamente.

## Instalación (usuario final)

1. Descarga `SE_HardwareInserterSetup-<versión>.exe` desde la
   [página de Releases](../../releases) de este repositorio, o cópialo desde un pendrive — es un
   único archivo, sin dependencias externas ni conexión a internet requerida durante la instalación.
2. Ejecútalo con doble clic. No requiere permisos de administrador (se instala en tu perfil de
   usuario y se registra solo para tu usuario).
   - Los artefactos oficiales deben tener una firma Authenticode válida. Comprueba firma y
     publicador antes de ejecutarlos; las compilaciones locales de desarrollo no están firmadas.
3. El instalador se detiene si Solid Edge está abierto. Ciérralo sin forzar su terminación, instala
   y después abre Solid Edge 2022-2026 con un ensamblaje **guardado en disco**.
4. En la ribbon del entorno de ensamblaje debería aparecer un botón **"Insertar Hardware"** en la
   categoría "Hardware". Si no aparece, revisa la sección de solución de problemas más abajo.

### Requisitos

- Windows con Solid Edge 64-bit 2022-2026 instalado.
- .NET Framework 4.8 (viene preinstalado en Windows 10/11 actualizados).

## Primer uso

1. Pulsa "Insertar Hardware" para abrir el panel.
2. Pulsa **"Carpeta..."** y selecciona la carpeta raíz donde tienes tus archivos `.par` de
   tornillería (las subcarpetas anidadas se reproducen recursivamente en el árbol). Esta
   ruta se recuerda para la próxima vez.
3. En Solid Edge, selecciona con Ctrl+click la cara cilíndrica interior del agujero donde quieres
   insertar el tornillo (y opcionalmente su cara plana de apoyo).
4. Pulsa **"Capturar Agujero"** — debería mostrarse el diámetro detectado. Verifica que el valor
   sea razonable (esta es la primera validación real de la conversión de unidades del add-in).
5. Selecciona un archivo `.par` del árbol e pulsa **"Insertar"** (o doble clic). Si el checkbox
   "Aplicar restricciones" está marcado, primero se intenta resolver automáticamente y, si hace
   falta, se conserva la occurrence grounded para seleccionar de forma guiada el cilindro y el
   apoyo. Después puedes aplicar las caras, conservar la pieza libre o cancelar; cerrar el panel
   revierte cualquier inserción pendiente. Si lo desmarcas, la copia SHA-256 se prepara de forma
   asíncrona antes de las llamadas COM y la pieza se inserta libre.

## Solución de problemas

- **No aparece el botón en la ribbon**: comprueba que el registro COM se completó — abre
  `regedit` y busca `HKEY_CURRENT_USER\Software\Solid Edge\Add-Ins`; debería existir la subclave
  `{DC87125A-6DBF-43A5-968B-2578CCBFC158}`. Si no está, reinstala o ejecuta manualmente
  `dev/register-addin.ps1` desde el código fuente.
- **"Guarde el ensamblaje antes de insertar hardware"**: el add-in necesita que el ensamblaje
  activo ya tenga una ruta en disco (guárdalo primero con Ctrl+S).
- **Diámetro capturado no parece correcto**: repórtalo — la conversión de unidades (metros→mm) aún
  no está validada contra todas las configuraciones de documento.
- **Diagnóstico**: los logs diarios se guardan en `%LocalAppData%\SE_HardwareInserter\logs`, rotan
  al alcanzar 5 MB y se conservan durante 14 días.

## Desarrollo

Para compilar desde el código fuente, ejecutar tests o entender la arquitectura del proyecto
(add-in COM, no app standalone), consulta [`AGENTS.md`](AGENTS.md).
El registro COM per-user, el golden de RegAsm y el límite seguro de migración se documentan en
[`docs/COM-registration.md`](docs/COM-registration.md).

Para generar el instalador localmente (Windows, con [Inno Setup 6.7.3+](https://jrsoftware.org/isinfo.php)
instalado):

```powershell
.\dev\check-prerequisites.ps1 -Profile Packaging
dotnet restore SE_HardwareInserter.slnx --locked-mode
dotnet build src\SE_HardwareInserter.AddIn\SE_HardwareInserter.AddIn.csproj -c Release -p:Platform=x64 --no-restore
ISCC.exe /DMyAppVersion=1.0.0 installer\SE_HardwareInserter.iss
# -> installer\Output\SE_HardwareInserterSetup-1.0.0.exe
.\dev\smoke-install.ps1 -InstallerPath .\installer\Output\SE_HardwareInserterSetup-1.0.0.exe
.\dev\smoke-uninstall.ps1
```

Los cuatro `packages.lock.json` están versionados y las compilaciones normales usan restauración
bloqueada. Ejecuta un restore sin bloqueo solo al actualizar dependencias y revisa los cuatro diffs.

Los pull requests y pushes a `main` deben superar el CI de build/tests x64 en Windows y el análisis
C# independiente de CodeQL. Dependabot propone semanalmente actualizaciones de NuGet y GitHub
Actions contra `main`. La publicación firmada de producción permanece bloqueada hasta disponer de
cuenta, perfil Public Trust y federación OIDC válidos en Azure Artifact Signing; las credenciales de
firma nunca se almacenan en el repositorio.
También se bloquea hasta disponer de evidencias reales `SE2022.json` a `SE2026.json`. Sigue
[`validation/solid-edge/README.md`](validation/solid-edge/README.md): valida un source commit,
añade los cinco resultados juntos en un commit exclusivo de evidencias y etiqueta ese commit.
CI proporciona el candidato de aceptación sin firmar y `source-commit.txt`; la release final
reconstruye con locks, firma un hash binario distinto y lo valida con el smoke del instalador.

Un push de un tag `vX.Y.Z` dispara el workflow `.github/workflows/release.yml`, que compila,
genera el instalador y lo publica como asset del Release de GitHub.

## Licencia

[MIT](LICENSE)
