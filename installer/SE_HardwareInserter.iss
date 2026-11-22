; Instalador per-user del add-in COM SE_HardwareInserter.
; Requiere Inno Setup 6.7.3 y un build Release x64 previo:
;   dotnet restore SE_HardwareInserter.slnx
;   dotnet build src\SE_HardwareInserter.AddIn\SE_HardwareInserter.AddIn.csproj -c Release -p:Platform=x64 --no-restore
;   ISCC.exe /DMyAppVersion=1.0.0 installer\SE_HardwareInserter.iss

#if VER < EncodeVer(6, 7, 3)
  #error Este instalador requiere Inno Setup 6.7.3 o posterior.
#endif

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef BuildOutputDir
  #define BuildOutputDir "..\src\SE_HardwareInserter.AddIn\bin\x64\Release\net48"
#endif

#define MyAppName "SE_HardwareInserter"
#define MyAppPublisher "Gabriel Spoleto"
#define MyAppId "{{7B7C6B2E-9C7D-4B2E-9E2B-6F5F6F5A1B3B}"
#define MyAppIdText "{7B7C6B2E-9C7D-4B2E-9E2B-6F5F6F5A1B3B}"
#define UninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppIdText}_is1"
#define AddInDll "SE_HardwareInserter.AddIn.dll"
#define AddInClass "SE_HardwareInserter.AddIn.SE_HardwareInserterAddIn"
#define AddInAssembly "SE_HardwareInserter.AddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
#define AddInClsid "{{DC87125A-6DBF-43A5-968B-2578CCBFC158}"
#define AddInClsidText "{DC87125A-6DBF-43A5-968B-2578CCBFC158}"
#define ManagedCategory "{{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}"
; Solid Edge enumera sus add-ins por estas categorías COM; sin ellas no carga el CLSID.
#define SolidEdgeAddInCategory "{{26B1D2D1-2B03-11D2-B589-080036E8B802}"
#define SolidEdgeAddInCategoryText "{26B1D2D1-2B03-11D2-B589-080036E8B802}"
#define AssemblyEnvironmentCategory "{{26618395-09D6-11D1-BA07-080036230602}"
#define AssemblyEnvironmentCategoryText "{26618395-09D6-11D1-BA07-080036230602}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\{#MyAppName}
UsePreviousAppDir=no
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no
RestartApplications=no
OutputDir=Output
OutputBaseFilename=SE_HardwareInserterSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AddInDll}
DisableFinishedPage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "{#BuildOutputDir}\SE_HardwareInserter.AddIn.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutputDir}\SE_HardwareInserter.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutputDir}\SE_HardwareInserter.SolidEdge.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutputDir}\Interop.SolidEdge.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dev\installer-helper.ps1"; Flags: dontcopy

; Contrato COM per-user equivalente al golden de RegAsm /regfile.
[Registry]
Root: HKCU64; Subkey: "Software\Classes\SE_HardwareInserter.AddIn"; ValueType: string; ValueName: ""; ValueData: "{#AddInClass}"; Flags: uninsdeletekey
Root: HKCU64; Subkey: "Software\Classes\SE_HardwareInserter.AddIn\CLSID"; ValueType: string; ValueName: ""; ValueData: "{#AddInClsid}"
Root: HKCU64; Subkey: "Software\Classes\HardwareInserter.AddIn"; ValueType: string; ValueName: ""; ValueData: "{#AddInClass}"; Flags: uninsdeletekey
Root: HKCU64; Subkey: "Software\Classes\HardwareInserter.AddIn\CLSID"; ValueType: string; ValueName: ""; ValueData: "{#AddInClsid}"
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}"; ValueType: string; ValueName: ""; ValueData: "{#AddInClass}"; Flags: uninsdeletekey
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "mscoree.dll"
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Both"
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\InprocServer32"; ValueType: string; ValueName: "Class"; ValueData: "{#AddInClass}"
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\InprocServer32"; ValueType: string; ValueName: "Assembly"; ValueData: "{#AddInAssembly}"
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\InprocServer32"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\InprocServer32"; ValueType: string; ValueName: "CodeBase"; ValueData: "{code:GetAddInCodeBase}"
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\InprocServer32\1.0.0.0"; ValueType: string; ValueName: ""; ValueData: "mscoree.dll"
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\InprocServer32\1.0.0.0"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Both"
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\InprocServer32\1.0.0.0"; ValueType: string; ValueName: "Class"; ValueData: "{#AddInClass}"
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\InprocServer32\1.0.0.0"; ValueType: string; ValueName: "Assembly"; ValueData: "{#AddInAssembly}"
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\InprocServer32\1.0.0.0"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\InprocServer32\1.0.0.0"; ValueType: string; ValueName: "CodeBase"; ValueData: "{code:GetAddInCodeBase}"
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\ProgId"; ValueType: string; ValueName: ""; ValueData: "SE_HardwareInserter.AddIn"
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\Implemented Categories\{#ManagedCategory}"; ValueType: none
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}"; ValueType: dword; ValueName: "AutoConnect"; ValueData: "1"
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\Implemented Categories\{#SolidEdgeAddInCategory}"; ValueType: none
Root: HKCU64; Subkey: "Software\Classes\CLSID\{#AddInClsid}\Environment Categories\{#AssemblyEnvironmentCategory}"; ValueType: none
Root: HKCU64; Subkey: "Software\Solid Edge\Add-Ins\{#AddInClsid}"; ValueType: string; ValueName: "Description"; ValueData: "SE_HardwareInserter: inserción de tornillería y anchors desde librería local."; Flags: uninsdeletekey
Root: HKCU64; Subkey: "Software\Solid Edge\Add-Ins\{#AddInClsid}"; ValueType: string; ValueName: "Summary"; ValueData: "Add-in para inserción automática de hardware en ensamblajes de Solid Edge."
Root: HKCU64; Subkey: "Software\Solid Edge\Add-Ins\{#AddInClsid}"; ValueType: dword; ValueName: "Visible"; ValueData: "1"

[Code]
var
  CachedCodeBase: String;

function IsSolidEdgeRunning(): Boolean;
var
  SolidEdgeApplication: Variant;
begin
  Result := False;
  try
    SolidEdgeApplication := GetActiveOleObject('SolidEdge.Application');
    Result := True;
  except
    Result := False;
  end;
end;

function ConfirmSolidEdgeClosed(const ActionText: String): Boolean;
begin
  Result := not IsSolidEdgeRunning();
  if not Result then
    MsgBox(
      'Solid Edge está abierto. Guarde su trabajo y ciérrelo antes de ' + ActionText + '.',
      mbError,
      MB_OK);
end;

function ExtractUninstallerPath(CommandLine: String): String;
var
  ClosingQuote: Integer;
begin
  CommandLine := Trim(CommandLine);
  Result := CommandLine;
  if (Length(CommandLine) > 0) and (CommandLine[1] = '"') then
  begin
    Delete(CommandLine, 1, 1);
    ClosingQuote := Pos('"', CommandLine);
    if ClosingQuote = 0 then
      Result := ''
    else
      Result := Copy(CommandLine, 1, ClosingQuote - 1);
  end;
end;

function HandlePreviousSameAppId(): Boolean;
var
  CommandLine: String;
  UninstallerPath: String;
  CurrentUninstaller: String;
  LegacyUninstaller: String;
  ResultCode: Integer;
begin
  Result := True;
  if not RegQueryStringValue(HKCU64, '{#UninstallKey}', 'UninstallString', CommandLine) then
    Exit;

  UninstallerPath := ExtractUninstallerPath(CommandLine);
  CurrentUninstaller := ExpandConstant('{localappdata}\SE_HardwareInserter\unins000.exe');
  LegacyUninstaller := ExpandConstant('{localappdata}\HardwareInserter\unins000.exe');
  if CompareText(UninstallerPath, CurrentUninstaller) = 0 then
    Exit;

  if (CompareText(UninstallerPath, LegacyUninstaller) <> 0) or
     (not FileExists(LegacyUninstaller)) then
  begin
    Log('Ambiguous previous uninstall command: ' + CommandLine);
    SuppressibleMsgBox(
      'Existe una instalación previa con el mismo AppId en una ubicación no reconocida. ' +
      'Desinstálela manualmente antes de continuar.',
      mbError,
      MB_OK,
      IDOK);
    Result := False;
    Exit;
  end;

  ResultCode := -1;
  Result := Exec(
    LegacyUninstaller,
    '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) and (ResultCode = 0);
  if Result and RegKeyExists(HKCU64, '{#UninstallKey}') then
    Result := False;
  if not Result then
    Log(Format('Legacy uninstaller failed with exit code %d.', [ResultCode]));
end;

function InitializeSetup(): Boolean;
begin
  Result := ConfirmSolidEdgeClosed('instalar o actualizar SE_HardwareInserter');
  if Result then
    Result := HandlePreviousSameAppId();
end;

function InitializeUninstall(): Boolean;
begin
  Result := ConfirmSolidEdgeClosed('desinstalar SE_HardwareInserter');
end;

function GetAddInCodeBase(Param: String): String;
var
  HelperPath: String;
  OutputPath: String;
  Parameters: String;
  ResultCode: Integer;
  Lines: TStringList;
begin
  if CachedCodeBase <> '' then
  begin
    Result := CachedCodeBase;
    Exit;
  end;

  HelperPath := ExpandConstant('{tmp}\installer-helper.ps1');
  if not FileExists(HelperPath) then
    ExtractTemporaryFile('installer-helper.ps1');
  OutputPath := ExpandConstant('{tmp}\se-hardware-inserter-codebase.txt');
  DeleteFile(OutputPath);
  Parameters :=
    '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' +
    AddQuotes(HelperPath) + ' -AssemblyPath ' +
    AddQuotes(ExpandConstant('{app}\{#AddInDll}')) + ' -OutputPath ' +
    AddQuotes(OutputPath);
  ResultCode := -1;
  if not Exec(
      ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe'),
      Parameters,
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) or (ResultCode <> 0) then
    RaiseException('No se pudo calcular el CodeBase URI mediante el helper de 64 bits.');

  Lines := TStringList.Create;
  try
    Lines.LoadFromFile(OutputPath);
    if Lines.Count <> 1 then
      RaiseException('El helper devolvió un CodeBase URI no válido.');
    CachedCodeBase := Trim(Lines[0]);
    if CachedCodeBase = '' then
      RaiseException('El helper devolvió un CodeBase URI vacío.');
  finally
    Lines.Free;
    DeleteFile(OutputPath);
  end;
  Result := CachedCodeBase;
end;

function RegistryStringEquals(const KeyName: String; const ValueName: String;
  const Expected: String): Boolean;
var
  Value: String;
begin
  Result := RegQueryStringValue(HKCU64, KeyName, ValueName, Value) and
    (Value = Expected);
end;

function RegistryDwordEquals(const KeyName: String; const ValueName: String;
  Expected: Cardinal): Boolean;
var
  Visible: Cardinal;
begin
  Result := RegQueryDWordValue(HKCU64, KeyName, ValueName, Visible) and
    (Visible = Expected);
end;

function RegistrationReadbackIsValid(): Boolean;
var
  InprocPath: String;
  VersionedInprocPath: String;
  ClsidPath: String;
  SolidEdgePath: String;
begin
  ClsidPath := 'Software\Classes\CLSID\{#AddInClsidText}';
  InprocPath := ClsidPath + '\InprocServer32';
  VersionedInprocPath := InprocPath + '\1.0.0.0';
  SolidEdgePath := 'Software\Solid Edge\Add-Ins\{#AddInClsidText}';
  Result :=
    RegistryStringEquals('Software\Classes\SE_HardwareInserter.AddIn', '', '{#AddInClass}') and
    RegistryStringEquals('Software\Classes\SE_HardwareInserter.AddIn\CLSID', '', '{#AddInClsidText}') and
    RegistryStringEquals('Software\Classes\HardwareInserter.AddIn', '', '{#AddInClass}') and
    RegistryStringEquals('Software\Classes\HardwareInserter.AddIn\CLSID', '', '{#AddInClsidText}') and
    RegistryStringEquals(ClsidPath, '', '{#AddInClass}') and
    RegistryStringEquals(InprocPath, '', 'mscoree.dll') and
    RegistryStringEquals(InprocPath, 'ThreadingModel', 'Both') and
    RegistryStringEquals(InprocPath, 'Class', '{#AddInClass}') and
    RegistryStringEquals(InprocPath, 'Assembly', '{#AddInAssembly}') and
    RegistryStringEquals(InprocPath, 'RuntimeVersion', 'v4.0.30319') and
    RegistryStringEquals(InprocPath, 'CodeBase', GetAddInCodeBase('')) and
    RegistryStringEquals(VersionedInprocPath, '', 'mscoree.dll') and
    RegistryStringEquals(VersionedInprocPath, 'ThreadingModel', 'Both') and
    RegistryStringEquals(VersionedInprocPath, 'Class', '{#AddInClass}') and
    RegistryStringEquals(VersionedInprocPath, 'Assembly', '{#AddInAssembly}') and
    RegistryStringEquals(VersionedInprocPath, 'RuntimeVersion', 'v4.0.30319') and
    RegistryStringEquals(VersionedInprocPath, 'CodeBase', GetAddInCodeBase('')) and
    RegistryStringEquals(ClsidPath + '\ProgId', '', 'SE_HardwareInserter.AddIn') and
    RegKeyExists(HKCU64, ClsidPath + '\Implemented Categories\{#ManagedCategory}') and
    RegistryDwordEquals(ClsidPath, 'AutoConnect', 1) and
    RegKeyExists(HKCU64, ClsidPath + '\Implemented Categories\{#SolidEdgeAddInCategoryText}') and
    RegKeyExists(HKCU64, ClsidPath + '\Environment Categories\{#AssemblyEnvironmentCategoryText}') and
    RegistryStringEquals(SolidEdgePath, 'Description',
      'SE_HardwareInserter: inserción de tornillería y anchors desde librería local.') and
    RegistryStringEquals(SolidEdgePath, 'Summary',
      'Add-in para inserción automática de hardware en ensamblajes de Solid Edge.') and
    RegistryDwordEquals(SolidEdgePath, 'Visible', 1);
end;

function CanActivateRegisteredAddIn(): Boolean;
var
  HelperPath: String;
  Parameters: String;
  ResultCode: Integer;
begin
  HelperPath := ExpandConstant('{tmp}\installer-helper.ps1');
  if not FileExists(HelperPath) then
    ExtractTemporaryFile('installer-helper.ps1');
  Parameters :=
    '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' +
    AddQuotes(HelperPath) + ' -Activate -Clsid ' + AddQuotes('{#AddInClsidText}');
  ResultCode := -1;
  Result := Exec(
    ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe'),
    Parameters,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) and (ResultCode = 0);
  if not Result then
    Log(Format('64-bit COM activation helper failed with exit code %d.', [ResultCode]));
end;

procedure RemoveKnownLegacyFiles();
var
  LegacyDirectory: String;
begin
  LegacyDirectory := ExpandConstant('{localappdata}\HardwareInserter');
  DeleteFile(LegacyDirectory + '\HardwareInserter.AddIn.dll');
  DeleteFile(LegacyDirectory + '\HardwareInserter.Core.dll');
  DeleteFile(LegacyDirectory + '\HardwareInserter.SolidEdge.dll');
  DeleteFile(LegacyDirectory + '\Interop.SolidEdge.dll');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then
    Exit;

  if not RegistrationReadbackIsValid() then
    RaiseException('El registro COM instalado no coincide con el contrato esperado.');
  if not CanActivateRegisteredAddIn() then
    RaiseException('El add-in instalado no pudo activarse mediante su CLSID. La instalación se revertirá.');

  { Solo después de activar la versión nueva se retiran binarios legacy conocidos. }
  RemoveKnownLegacyFiles();
end;
