; Builds RemoteNest-Setup.exe from the framework-dependent publish output
; (publish-installer). The .NET Desktop Runtime is NOT bundled — see the runtime check
; in [Code]. For a no-prerequisites option, ship the self-contained portable build.

#define AppVersion "1.0.0"

[Setup]
AppId={{B5F2A1E3-7C4D-4A8F-9E6B-1D3F5A7C9E2B}
AppName=RemoteNest
AppVersion={#AppVersion}
AppVerName=RemoteNest {#AppVersion}
AppPublisher=RemoteNest
AppPublisherURL=https://github.com/xp3z41x/RemoteNest.Desktop
AppSupportURL=https://github.com/xp3z41x/RemoteNest.Desktop/issues
AppUpdatesURL=https://github.com/xp3z41x/RemoteNest.Desktop/releases
AppContact=https://github.com/xp3z41x/RemoteNest.Desktop/issues
AppCopyright=Copyright (C) 2026 RemoteNest
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=RemoteNest
VersionInfoDescription=RemoteNest Setup
VersionInfoTextVersion={#AppVersion}
VersionInfoCopyright=Copyright (C) 2026 RemoteNest
VersionInfoProductName=RemoteNest
VersionInfoProductVersion={#AppVersion}.0
VersionInfoProductTextVersion={#AppVersion}
DefaultDirName={autopf}\RemoteNest
DefaultGroupName=RemoteNest
OutputDir=output
OutputBaseFilename=RemoteNest-Setup
SetupIconFile=RemoteNest\Assets\app.ico
UninstallDisplayIcon={app}\RemoteNest.exe
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
MinVersion=10.0.17763
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish-installer\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\RemoteNest"; Filename: "{app}\RemoteNest.exe"
Name: "{group}\{cm:UninstallProgram,RemoteNest}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\RemoteNest"; Filename: "{app}\RemoteNest.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\RemoteNest.exe"; Description: "{cm:LaunchProgram,RemoteNest}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RuntimeUrl = 'https://dotnet.microsoft.com/download/dotnet/10.0/runtime';

function IsPortuguese(): Boolean;
begin
  Result := CompareText(ActiveLanguage(), 'brazilianportuguese') = 0;
end;

{ The app is framework-dependent, so it needs the .NET Desktop Runtime 10 (x64).
  Presence is decided by the shared framework folder, which exists for both
  per-machine and (rare) side-by-side installs. }
function HasDesktopRuntime10(): Boolean;
var
  Base: String;
  Found: TFindRec;
begin
  Result := False;
  Base := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if not DirExists(Base) then Exit;

  if FindFirst(Base + '\10.*', Found) then
  begin
    try
      repeat
        if (Found.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(Found);
    finally
      FindClose(Found);
    end;
  end;
end;

function ConfirmMissingRuntime(): Boolean;
var
  Msg: String;
  ErrorCode: Integer;
begin
  if IsPortuguese() then
    Msg := 'O RemoteNest precisa do .NET Desktop Runtime 10 (x64), que nao foi encontrado neste computador.' + #13#10 + #13#10 +
           'SIM = abrir a pagina de download agora (a instalacao sera cancelada)' + #13#10 +
           'NAO = instalar mesmo assim (o app so vai abrir depois que voce instalar o runtime)'
  else
    Msg := 'RemoteNest requires the .NET Desktop Runtime 10 (x64), which was not found on this computer.' + #13#10 + #13#10 +
           'YES = open the download page now (setup will exit)' + #13#10 +
           'NO = install anyway (the app will only start once the runtime is installed)';

  if MsgBox(Msg, mbConfirmation, MB_YESNO) = IDYES then
  begin
    ShellExec('open', RuntimeUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Result := False;
  end
  else
    Result := True;
end;

function InitializeSetup(): Boolean;
var
  Response: Integer;
  Params: String;
  ExePath: String;
  ErrorCode: Integer;
  I: Integer;
  HasFlag: Boolean;
  MsgText: String;
begin
  Result := True;
  try
    if not HasDesktopRuntime10() then
    begin
      Result := ConfirmMissingRuntime();
      if not Result then Exit;
    end;

    HasFlag := False;
    for I := 1 to ParamCount do
      if (CompareText(ParamStr(I), '/ALLUSERS') = 0) or (CompareText(ParamStr(I), '/CURRENTUSER') = 0) then
        HasFlag := True;
    if HasFlag then Exit;
    if WizardSilent() then Exit;

    if IsPortuguese() then
      MsgText := 'Escolha o modo de instalacao:' + #13#10 + #13#10 +
                 'SIM = Instalar para todos os usuarios (requer privilegios de administrador)' + #13#10 +
                 'NAO = Instalar apenas para o usuario atual (sem admin)' + #13#10 +
                 'CANCELAR = Sair da instalacao'
    else
      MsgText := 'Choose installation mode:' + #13#10 + #13#10 +
                 'YES = Install for all users (requires admin privileges)' + #13#10 +
                 'NO = Install for current user only (no admin required)' + #13#10 +
                 'CANCEL = Exit setup';

    Response := MsgBox(MsgText, mbConfirmation, MB_YESNOCANCEL);
    if Response = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
    if Response = IDYES then
      Params := '/ALLUSERS'
    else
      Params := '/CURRENTUSER';

    ExePath := ExpandConstant('{srcexe}');
    if not ShellExec('', ExePath, Params, '', SW_SHOWNORMAL, ewNoWait, ErrorCode) then
    begin
      MsgBox('Failed to relaunch setup. Error: ' + IntToStr(ErrorCode), mbError, MB_OK);
      Result := False;
      Exit;
    end;
    Result := False;
  except
    { Any Pascal failure falls through to Inno's native flow rather than failing silently. }
    Result := True;
  end;
end;
