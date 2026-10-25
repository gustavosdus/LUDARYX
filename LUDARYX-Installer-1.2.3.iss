; LUDARYX 1.2.3 - Instalador Inno Setup
; Editor: Gustavo José da Silva
;
; Antes de compilar este instalador, execute:
; powershell -ExecutionPolicy Bypass -File Scripts\Publish-Release.ps1
;
; O script publica LUDARYX.exe e o auxiliar LUDARYX.Updater.exe na mesma pasta.
; Ambos serão incluídos pelo bloco [Files] abaixo.

#define MyAppName "LUDARYX"
#define MyAppVersion "1.2.3"
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
OutputBaseFilename=LUDARYX-1.2.3-Setup
SetupIconFile=Assets\LUDARYX.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}

Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

VersionInfoVersion=1.2.3.0
VersionInfoProductVersion=1.2.3.0
VersionInfoProductName={#MyAppName}
VersionInfoDescription=LUDARYX Installer
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
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
brazilianportuguese.CreateDesktopIcon=Criar um atalho na Área de Trabalho
english.CreateDesktopIcon=Create a desktop shortcut
brazilianportuguese.AdditionalShortcuts=Atalhos adicionais:
english.AdditionalShortcuts=Additional shortcuts:
brazilianportuguese.LaunchLudaryx=Abrir LUDARYX
english.LaunchLudaryx=Launch LUDARYX
brazilianportuguese.AppRunningTitle=O LUDARYX está aberto.
english.AppRunningTitle=LUDARYX is currently running.
brazilianportuguese.AppRunningInstruction=Feche o programa, inclusive pela opção "Encerrar" da System Tray, e clique em OK para continuar.
english.AppRunningInstruction=Close the program, including from the "Exit" option in the system tray, then click OK to continue.
brazilianportuguese.InstallCancelled=Instalação cancelada pelo usuário.
english.InstallCancelled=Installation cancelled by the user.
brazilianportuguese.AppStillRunning=O LUDARYX ainda está em execução. Encerre o programa completamente e execute o instalador novamente.
english.AppStillRunning=LUDARYX is still running. Close the program completely and run the installer again.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalShortcuts}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LUDARYX"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\LUDARYX"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchLudaryx}"; Flags: nowait postinstall skipifsilent runasoriginaluser

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
      CustomMessage('AppRunningTitle') + #13#10 + #13#10 +
      CustomMessage('AppRunningInstruction'),
      mbInformation,
      MB_OKCANCEL
    ) = IDCANCEL then
    begin
      Result := CustomMessage('InstallCancelled');
      Exit;
    end;

    if IsAppRunning() then
      Result := CustomMessage('AppStillRunning');
  end;
end;
