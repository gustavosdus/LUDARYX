; LUDARYX 1.0.3 - Instalador Inno Setup
; Editor: Gustavo José da Silva
;
; Antes de compilar este instalador, execute:
; powershell -ExecutionPolicy Bypass -File Scripts\Publish-Release.ps1
;
; O script publica LUDARYX.exe e o auxiliar LUDARYX.Updater.exe na mesma pasta.
; Ambos serão incluídos pelo bloco [Files] abaixo.

#define MyAppName "LUDARYX"
#define MyAppVersion "1.0.3"
#define MyAppPublisher "Gustavo José da Silva"
#define MyAppExeName "LUDARYX.exe"
#define MyAppId "{{8F9AB4E7-7C65-4F0F-9D89-7BBD0F5AE2C1}"
#define PublishDir "bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\LUDARYX
DefaultGroupName=LUDARYX
DisableProgramGroupPage=yes

OutputDir=Installer
OutputBaseFilename=LUDARYX-1.0.3-Setup
SetupIconFile=Assets\LUDARYX.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}

Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

VersionInfoVersion=1.0.3.0
VersionInfoProductVersion=1.0.3.0
VersionInfoProductName={#MyAppName}
VersionInfoDescription=Instalador do LUDARYX
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright=Copyright (C) 2026 {#MyAppPublisher}

CloseApplications=yes
RestartApplications=no

; Quando você tiver um certificado de assinatura de código e configurar
; uma ferramenta de assinatura no Inno Setup, descomente estas linhas:
; SignTool=LUDARYXSign $f
; SignedUninstaller=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar um atalho na Área de Trabalho"; GroupDescription: "Atalhos adicionais:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LUDARYX"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\LUDARYX"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir LUDARYX"; Flags: nowait postinstall skipifsilent runasoriginaluser

; Não usamos [UninstallDelete] recursivo em {app}: o Inno Setup remove apenas
; os arquivos que pertencem à instalação, preservando arquivos alheios em pasta personalizada.

[Code]
function IsAppRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result :=
    Exec(
      ExpandConstant('{cmd}'),
      '/C tasklist /FI "IMAGENAME eq {#MyAppExeName}" | find /I "{#MyAppExeName}" >nul',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode
    ) and (ResultCode = 0);
end;

procedure StopAppForUninstall();
var
  ResultCode: Integer;
begin
  { O LUDARYX pode estar invisível na System Tray e intercepta WM_CLOSE para se ocultar.
    Durante a desinstalação precisamos encerrar o processo antes de remover os arquivos. }
  if IsAppRunning() then
  begin
    Exec(
      ExpandConstant('{cmd}'),
      '/C taskkill /IM "{#MyAppExeName}" /T /F >nul 2>&1',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode
    );
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopAppForUninstall();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';

  if IsAppRunning() then
  begin
    if MsgBox(
      'O LUDARYX está aberto.' + #13#10 + #13#10 +
      'Feche o programa, inclusive pela opção "Encerrar" da System Tray, e clique em OK para continuar.',
      mbInformation,
      MB_OKCANCEL
    ) = IDCANCEL then
    begin
      Result := 'Instalação cancelada pelo usuário.';
      Exit;
    end;

    if IsAppRunning() then
      Result := 'O LUDARYX ainda está em execução. Encerre o programa completamente e execute o instalador novamente.';
  end;
end;
