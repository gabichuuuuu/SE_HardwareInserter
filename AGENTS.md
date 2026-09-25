# SE_HardwareInserter: instrucciones del proyecto

`AGENTS.md` es la única fuente de instrucciones para todos los agentes; Claude Code la carga mediante `agents-md`. No crees `CLAUDE.md`: desactivaría esa carga. Consulta solo el código y documentación relacionados con la tarea.

## Arquitectura y compatibilidad

Add-in COM cargado por Solid Edge mediante `ISolidEdgeAddIn.OnConnection`; recibe la instancia de `Application`. No es una macro ni un EXE y no obtiene la aplicación mediante `Marshal.GetActiveObject`. Abre la ventana WPF no modal `HardwarePickerWindow` desde la ribbon de ensamblaje.

| Proyecto | Responsabilidad |
| --- | --- |
| `SE_HardwareInserter.Core` | Dominio, `FileSystemFastenerLibrary`, `Settings/SettingsStore`, `LocalHardwareCache/LocalHardwareCopy`; sin COM |
| `SE_HardwareInserter.SolidEdge` | Captura, inserción, restricciones y resolución geométrica con interop |
| `SE_HardwareInserter.AddIn` | Ciclo de vida COM, WPF y coordinación de Core con Solid Edge |
| `SE_HardwareInserter.Core.Tests` | xUnit y FluentAssertions; lógica sin COM |

- `Directory.Build.props` fija `net48`, C# 9, nullable y errores de nulabilidad. Conserva esos límites por compatibilidad COM; una skill genérica no autoriza migrarlos.
- Compila el add-in en x64. `Interop.SolidEdge` usa `EmbedInteropTypes=false` y se distribuye con el add-in; cambiarlo exige validar registro, eventos y arranque reales.
- La solución es `SE_HardwareInserter.slnx`. Los cuatro locks de NuGet están versionados: restauración bloqueada en builds normales y CI. Solo en un cambio de dependencias autorizado, restaura sin bloqueo, revisa los cuatro diffs y vuelve a `--locked-mode`.
- Comentarios y XML docs en español; identificadores en inglés. Documenta contratos públicos y decisiones no evidentes. `GenerateDocumentationFile=true` y `NoWarn 1591` no convierten cada detalle interno en documentación obligatoria.

## Biblioteca, inserción y recursos

El catálogo escanea recursivamente archivos `*.par`; no usa índice JSON ni importación Excel. El usuario elige la raíz con «Carpeta...» y se guarda únicamente `RootPath` en `%AppData%\SE_HardwareInserter\settings.json`. El watcher conserva el árbol vivo, `RelativePath` y `Filename` sin inferir metadatos de nombres.

Antes de insertar, `LocalHardwareCache.EnsureLocalCopy` crea una copia inmutable por SHA-256 en `Hardware/.se-hardware-inserter/<verified-hash-prefix>/`. El manifiesto usa rutas relativas al ensamblaje. No sobrescribas archivos legacy de `Hardware/`; pasa `LocalHardwareCopy.LocalPath` a Solid Edge.

Solo el trabajo de copia se ejecuta fuera del hilo STA; las llamadas COM vuelven al hilo de Solid Edge. Conserva captura de la cara cilíndrica del agujero y apoyo opcional, clasificación geométrica y flujo automática → selección guiada → conservar libre o cancelar. Ambigüedad o topología insuficiente no autorizan escoger una cara arbitraria. Cerrar la ventana revierte la occurrence pendiente.

Los logs son best-effort en `%LocalAppData%\SE_HardwareInserter\logs`, rotan a 5 MB y se conservan 14 días. `operationId` relaciona copia, inserción, restricciones, commit y rollback.

## Contratos COM

Verifica nuevas firmas y enums contra el ensamblado referenciado o type library. Comprueba las herramientas e instalación actuales; una nota histórica de `monodis` no prueba ejecución real.

- `ISolidEdgeAddIn`: `OnConnection(Application, SeConnectMode, AddIn)`, `OnConnectToEnvironment`, `OnDisconnection`. Convierte el parámetro AddIn a `ISEAddInEx` para información y botones.
- Suscribe `OnCommand/OnCommandUpdateUI` mediante `AddInEventsClass`; `ISEAddInEventsEx` no tiene el wrapper de eventos .NET.
- CatID Assembly: `26618395-09D6-11d1-BA07-080036230602`.
- `SetAddInInfoEx` recibe `ref System.Array` para nombres e IDs, no `ref string[]` o `ref int[]`.
- Flags: `seCmdActive_Enabled=0x1`, `seCmdActive_Remove=0x40`.
- `Body.Faces[FeatureTopologyQueryTypeConstants.igQueryAll]` es un indexador; llamarlo como método produce `CS1955`.
- Para inserción/captura consulta `Occurrences.AddByFilename`, `Relations3d.AddAxial/AddPlanar`, `Occurrence.Body`, `Face.Geometry/Area`, `AssemblyDocument.Path/Occurrences/Relations3d` y `Application.ActiveDocument/ActiveSelectSet` en la interop efectiva.

La conversión de metros a mm usa 1000; valida unidades contra un documento real antes de aceptar medidas. La clasificación geométrica, ribbon y compatibilidad SE2022-SE2026 necesitan aceptación real. Si no aparece el botón, comprueba HKCU, bitmap e identificador del entorno. La versión NuGet 220.2.0 por sí sola no demuestra compatibilidad de runtime.

## Comandos y validación

```powershell
dotnet restore SE_HardwareInserter.slnx --locked-mode
dotnet build src/SE_HardwareInserter.AddIn/SE_HardwareInserter.AddIn.csproj -c Debug -p:Platform=x64 --no-restore
dotnet test tests/SE_HardwareInserter.Core.Tests/SE_HardwareInserter.Core.Tests.csproj --no-restore
.\dev\check-prerequisites.ps1
```

Usa `--filter "FullyQualifiedName~<nombre>"` para una regresión concreta y `-Profile Packaging` en el diagnóstico de empaquetado. En implementación completa el comportamiento solicitado y corrige fallos propios con comprobaciones pertinentes. En cambios solo de instrucciones revisa diff y referencias; no requiere compilar o registrar COM.

En Linux puede faltar `testhost.net48.exe`. Usa Windows o un runner Mono/xUnit compatible existente; no añadas y reviertas paquetes por rutina para sortear el host. Declara qué prueba no pudo ejecutarse. Tests de lógica o gateways simulados no demuestran integración COM.

Registro local autorizado: `dev/register-addin.ps1` (opcional `-Config Release`); retirada: `dev/unregister-addin.ps1`. Usan el contrato per-user HKCU de 64 bits en `dev/SEHardwareInserter.Registration.psm1`; RegAsm solo regenera el golden. Estos comandos modifican registro y no son checks documentales.

## Publicación

Para release consulta `validation/solid-edge/README.md`. Conserva build/tests Windows x64 y CodeQL en PRs/pushes a main. Dependabot actualiza NuGet y Actions contra main.

Una release firmada exige Azure Artifact Signing, perfil Public Trust y OIDC validados. El tag apunta al commit exclusivo con evidencias `SE2022.json`–`SE2026.json`; su padre es el `sourceCommitSha` validado y ese commit no admite otros cambios. El candidato unsigned de CI queda ligado a `GITHUB_SHA` por `source-commit.txt` y solo sirve para la matriz; release reconstruye locked, firma el binario final y ejecuta su smoke.

No versionar `bin/`, `obj/`, `.vs/`, `*.user` ni secretos de firma.
