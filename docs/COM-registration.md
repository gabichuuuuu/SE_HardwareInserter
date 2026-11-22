# Contrato de registro COM per-user

El instalador y los scripts de desarrollo registran el add-in directamente en la vista de 64 bits
de `HKCU`. No ejecutan `RegAsm` en el equipo del usuario.

## Golden de RegAsm

El 4 de septiembre de 2026 se compiló temporalmente el código actual como `net48`, C# 9,
`x64` y `AssemblyVersion=1.0.0.0` contra `Interop.SolidEdge` 220.2.0. Después se ejecutó:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" SE_HardwareInserter.AddIn.dll /regfile:SE_HardwareInserter.reg /codebase
```

`RegAsm 4.8.9221.0` generó el siguiente contrato. Las rutas se normalizan a `HKCU\Software\Classes`
y el `CodeBase` usa un marcador porque el instalador decide la ubicación final:

```text
SE_HardwareInserter.AddIn
  (Default) = SE_HardwareInserter.AddIn.SE_HardwareInserterAddIn
  CLSID\(Default) = {DC87125A-6DBF-43A5-968B-2578CCBFC158}

CLSID\{DC87125A-6DBF-43A5-968B-2578CCBFC158}
  (Default) = SE_HardwareInserter.AddIn.SE_HardwareInserterAddIn
  ProgId\(Default) = SE_HardwareInserter.AddIn
  Implemented Categories\{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}
  InprocServer32\(Default) = mscoree.dll
  InprocServer32\ThreadingModel = Both
  InprocServer32\Class = SE_HardwareInserter.AddIn.SE_HardwareInserterAddIn
  InprocServer32\Assembly = SE_HardwareInserter.AddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
  InprocServer32\RuntimeVersion = v4.0.30319
  InprocServer32\CodeBase = {ADDIN_CODEBASE_URI}
  InprocServer32\1.0.0.0\Class = SE_HardwareInserter.AddIn.SE_HardwareInserterAddIn
  InprocServer32\1.0.0.0\Assembly = SE_HardwareInserter.AddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
  InprocServer32\1.0.0.0\RuntimeVersion = v4.0.30319
  InprocServer32\1.0.0.0\CodeBase = {ADDIN_CODEBASE_URI}
```

La advertencia de `RegAsm /codebase` sobre el ensamblado temporal sin strong name no afecta al
contrato. El pipeline de release debe firmar las tres DLL propias mediante Azure Artifact Signing
antes de construir el instalador.

## Extensiones controladas

`RegAsm /regfile` no ejecuta `ComRegisterFunction` ni conoce Solid Edge. El proyecto añade:

- `HardwareInserter.AddIn` como alias de migración hacia el mismo CLSID;
- `HKCU\Software\Solid Edge\Add-Ins\{DC87125A-6DBF-43A5-968B-2578CCBFC158}` con
  `Description`, `Summary` y `Visible=1`;
- bajo `CLSID\{DC87125A-6DBF-43A5-968B-2578CCBFC158}`: el valor `AutoConnect=1`, la categoría de
  add-in de Solid Edge `Implemented Categories\{26B1D2D1-2B03-11D2-B589-080036E8B802}` y la de
  entorno `Environment Categories\{26618395-09D6-11D1-BA07-080036230602}` (Assembly).

Sin esas claves el CLSID no aparece en el Add-In Manager aunque exista la clave `Add-Ins`; se
validó el conjunto, no cuál de las tres es imprescindible. Validado el 22 de noviembre de 2026 en Solid Edge 2026
(226.00.00.106) con un registro limpio hecho por `dev/register-addin.ps1`: el add-in conecta al
entrar en el entorno de ensamblaje y crea la pestaña «Hardware».

El instalador valida por lectura estos valores y activa el CLSID antes de dar la instalación por
correcta. La desinstalación elimina únicamente las claves anteriores y los cuatro binarios
empaquetados. La migración solo intenta retirar estos archivos conocidos de
`%LocalAppData%\HardwareInserter`:

- `HardwareInserter.AddIn.dll`;
- `HardwareInserter.Core.dll`;
- `HardwareInserter.SolidEdge.dll`;
- `Interop.SolidEdge.dll`.

No se elimina la carpeta legacy, logs, configuración ni ningún archivo de procedencia ambigua.
