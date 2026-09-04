# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Comandos comunes

```bash
# Restaurar exactamente el grafo de dependencias versionado
dotnet restore SE_HardwareInserter.slnx --locked-mode

# Compilación canónica del add-in x64 (incluye Core y SolidEdge por ProjectReference)
dotnet build src/SE_HardwareInserter.AddIn/SE_HardwareInserter.AddIn.csproj -c Debug -p:Platform=x64 --no-restore

# Compilar un proyecto concreto
dotnet build src/SE_HardwareInserter.Core/SE_HardwareInserter.Core.csproj --no-restore
dotnet build src/SE_HardwareInserter.SolidEdge/SE_HardwareInserter.SolidEdge.csproj --no-restore

# Ejecutar todos los tests
dotnet test tests/SE_HardwareInserter.Core.Tests/SE_HardwareInserter.Core.Tests.csproj --no-restore

# Ejecutar un test concreto (por nombre parcial de método)
dotnet test tests/SE_HardwareInserter.Core.Tests/ --no-restore --filter "FullyQualifiedName~Refresh_CarpetaConPar"

# Diagnosticar el toolchain local sin instalar nada
.\dev\check-prerequisites.ps1
```

Los cuatro `packages.lock.json` están versionados y la restauración bloqueada es obligatoria en CI
y compilaciones normales. Para cambiar dependencias intencionadamente, se ejecuta una restauración
normal, se revisan los cuatro diffs y después se vuelve a `--locked-mode`.

## Gates de CI y publicación

- Los pull requests y pushes a `main` deben pasar build/tests x64 en Windows y el workflow
  independiente `.github/workflows/codeql.yml` para C#.
- Dependabot revisa semanalmente NuGet y las acciones de GitHub, siempre contra `main`.
- Una release firmada no debe publicarse hasta que Azure Artifact Signing disponga de cuenta,
  perfil Public Trust y federación OIDC validados. Los secretos de firma no se versionan.
- El tag debe apuntar a un commit exclusivo con las cinco evidencias `SE2022.json`-`SE2026.json`;
  su padre es el `sourceCommitSha` validado y el gate rechaza cualquier otro cambio en ese commit.
  El procedimiento completo está en `validation/solid-edge/README.md`.
- CI publica un candidato unsigned ligado a `GITHUB_SHA` mediante `source-commit.txt`. Es válido
  solo para la matriz; release reconstruye locked, firma el binario final y ejecuta su smoke.

> `dotnet test` requiere un testhost de .NET Framework 4.8, que no viene incluido en el SDK de
> dotnet en Linux. Si falla con `Cannot open assembly '.../testhost.net48.exe'`, compila el
> proyecto de tests y ejecuta el DLL resultante con Mono y el runner de xUnit:
> `mono ~/.nuget/packages/xunit.runner.console/<version>/tools/net48/xunit.console.exe bin/Debug/net48/SE_HardwareInserter.Core.Tests.dll`
> (instala el paquete `xunit.runner.console` temporalmente con `dotnet add package` solo para
> esto y revierte el cambio en el `.csproj` después; no debe quedar como dependencia permanente).
> Los tests de `SE_HardwareInserter.SolidEdge`/`SE_HardwareInserter.AddIn` (si se añaden en el futuro)
> requerirían Solid Edge real instalado, ya que dependen de la interop COM.

## Arquitectura

**Target:** .NET Framework 4.8 (`net48`) con C# 9, `Nullable` habilitado (`WarningsAsErrors`).
Fijado en `Directory.Build.props` para máxima compatibilidad con la interop COM de Solid Edge.

**Forma de ejecución:** add-in COM registrado, NO macro/EXE standalone. Solid Edge carga el
ensamblado `SE_HardwareInserter.AddIn` al arrancar (registrado en
`HKCU\Software\Solid Edge\Add-Ins\{CLSID}`) e invoca `ISolidEdgeAddIn.OnConnection`, recibiendo la
instancia de `Application` directamente (sin `Marshal.GetActiveObject`). El add-in registra un
botón en la ribbon del entorno de ensamblaje; al pulsarlo, abre el diálogo WPF no modal
`HardwarePickerWindow` que flota sobre Solid Edge.

### Proyectos

| Proyecto | Responsabilidad |
|---|---|
| `SE_HardwareInserter.Core` | Modelos (`LibraryItem`), librería basada en FS (`FileSystemFastenerLibrary`), preferencias (`Settings`/`SettingsStore`) y caché inmutable por contenido (`LocalHardwareCache`/`LocalHardwareCopy`). Sin dependencia de COM. |
| `SE_HardwareInserter.SolidEdge` | Toda la interop con `Interop.SolidEdge`: captura de geometría e inserción/restricciones (`SolidEdgeHelper`) y resolución de caras (`IScrewGeometryResolver`). |
| `SE_HardwareInserter.AddIn` | Add-in COM (`ISolidEdgeAddIn`) + WPF `HardwarePickerWindow`; coordina la caché Core con Solid Edge. Registro declarativo per-user en la vista HKCU de 64 bits. |
| `SE_HardwareInserter.Core.Tests` | Tests xUnit + FluentAssertions de `FileSystemFastenerLibrary` y `Settings`. Sin COM. |

### Flujo de la UI (`HardwarePickerWindow`)

1. **Cargar librería** → `FileSystemFastenerLibrary` escanea recursivamente `*.par` en la carpeta
   raíz configurada; el `FileSystemWatcher` refresca el árbol en vivo cuando se añaden/borran
   archivos. La UI reproduce recursivamente todas las subcarpetas y permite filtrar por texto libre.
2. **Configurar carpeta** → botón "Carpeta..." → `FolderBrowserDialog` → cambia `RootPath` y lo
   persiste en `settings.json`.
3. **Capturar Agujero** → `SolidEdgeHelper.CaptureSelectedHoleGeometry(application)` lee el
   `SelectSet` activo de Solid Edge. Se espera que el usuario haya seleccionado (Ctrl+click) la
   cara cilíndrica interior del agujero y, opcionalmente, su cara plana de apoyo, antes de pulsar
   el botón. Devuelve un `CapturedHoleGeometry` con el diámetro en mm.
4. **Insertar** → la UI ejecuta únicamente `LocalHardwareCache.EnsureLocalCopy` fuera del hilo STA,
   verifica SHA-256 y vuelve al hilo de Solid Edge antes de llamar a COM. La resolución sigue el
   orden automática → selección guiada → conservar libre o cancelar; cerrar la ventana revierte
   explícitamente cualquier occurrence pendiente. La caché inmutable no sobrescribe archivos
   legacy existentes directamente bajo `Hardware/`.

Los diagnósticos diarios se escriben best-effort en
`%LocalAppData%\SE_HardwareInserter\logs`, rotan a 5 MB y se conservan 14 días. Un mismo
`operationId` relaciona copia, inserción, restricciones, commit y rollback.

### Puntos de riesgo/extensión conocidos (no verificables sin Solid Edge real instalado)

- **Unidades**: `SolidEdgeHelper` asume que la API devuelve longitudes en metros y convierte a mm
  (`MetrosAMilimetros = 1000.0`). Validar contra un documento real antes de confiar en el
  diámetro mostrado.
- **Resolución de geometría del tornillo** (`IndexBasedScrewGeometryResolver`): adapta las caras
  analíticas a descriptores administrados y delega en `FastenerGeometryClassifier`, que compara
  diámetro, orientación, proximidad y dominancia. Un empate o topología insuficiente deriva al
  flujo guiado; no se elige silenciosamente la cara de mayor área.
- **Registro del botón en la ribbon**: `SetAddInInfoEx`/`AddCommandBarButton` están verificados por
  `monodis` pero NO probados en runtime. Si el botón no aparece, revisar: (1) registro en HKCU,
  (2) IDs de bitmap (actualmente 0 = sin icono), (3) CatID del entorno coincide con el tipo de
  documento activo.
- **`EmbedInteropTypes=false`**: el `Interop.SolidEdge.dll` (NuGet comunidad, 220.2.0 / SE 2022)
  se distribuye con el add-in. La compatibilidad con SE 2022-2026 debe demostrarse en la matriz de
  aceptación; la versión del paquete por sí sola no prueba la compatibilidad de ejecución.

### Catálogo de tornillería: escaneo de sistema de archivos (sin JSON/Excel)

El catálogo se construye dinámicamente escaneando una carpeta local recursivamente en busca de
`*.par`. No hay índice JSON ni importación Excel. La carpeta raíz la configura el usuario con el
botón "Carpeta..." y se persiste en `%AppData%\SE_HardwareInserter\settings.json` (solo `RootPath`).
Un `FileSystemWatcher` mantiene el árbol vivo: añadir un `.par` a la carpeta lo hace aparecer sin
reiniciar Solid Edge. El árbol conserva toda la jerarquía de `RelativePath`; cada hoja muestra
`Filename` tal cual, sin inferir metadatos desde el nombre.

### Firmas de Interop.SolidEdge verificadas

Las firmas de `Occurrences.AddByFilename`, `Relations3d.AddAxial/AddPlanar`, `Occurrence.Body`,
`Body.Faces[FeatureTopologyQueryTypeConstants]` (es un indexador C#, no un método — el compilador
lo confirma con el error `CS1955` si se llama como método), `Face.Geometry/Area`,
`AssemblyDocument.Path/Occurrences/Relations3d`, `Application.ActiveDocument/ActiveSelectSet`,
`ISolidEdgeAddIn.OnConnection/OnDisconnection`, `ISEAddInEx.SetAddInInfoEx/AddCommandBarButton/
AddInEvents`, `AddInEventsClass.OnCommand/OnCommandUpdateUI` fueron confirmadas desensamblando
`~/.nuget/packages/interop.solidedge/220.2.0/lib/net45/Interop.SolidEdge.dll` con `monodis` (no hay
Solid Edge real instalado en el entorno de desarrollo). Si se añaden nuevas llamadas a la API,
verificar igual contra ese DLL antes de asumir una firma.

CatID del entorno Assembly: `26618395-09D6-11d1-BA07-080036230602`
(extraído del `.cctor` de `SolidEdgeSDK.EnvironmentCategories` con `monodis`).
