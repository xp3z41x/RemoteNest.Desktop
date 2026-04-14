[Setup]
AppId={{B5F2A1E3-7C4D-4A8F-9E6B-1D3F5A7C9E2B}
AppName=RemoteNest
AppVersion=1.0.4
AppVerName=RemoteNest 1.0.4
AppPublisher=RemoteNest
DefaultDirName={autopf}\RemoteNest
DefaultGroupName=RemoteNest
OutputDir=output
OutputBaseFilename=RemoteNest-Setup
SetupIconFile=RemoteNest\Assets\app.ico
UninstallDisplayIcon={app}\RemoteNest.exe
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\RemoteNest"; Filename: "{app}\RemoteNest.exe"
Name: "{group}\{cm:UninstallProgram,RemoteNest}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\RemoteNest"; Filename: "{app}\RemoteNest.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\RemoteNest.exe"; Description: "{cm:LaunchProgram,RemoteNest}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  Response: Integer;
  Params: String;
  ExePath: String;
  ErrorCode: Integer;
  I: Integer;
  HasFlag: Boolean;
  IsPortuguese: Boolean;
  MsgText: String;
begin
  Result := True;
  HasFlag := False;
  for I := 1 to ParamCount do
    if (CompareText(ParamStr(I), '/ALLUSERS') = 0) or (CompareText(ParamStr(I), '/CURRENTUSER') = 0) then
      HasFlag := True;
  if HasFlag then Exit;
  if WizardSilent() then Exit;

  IsPortuguese := CompareText(ActiveLanguage(), 'brazilianportuguese') = 0;
  if IsPortuguese then
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
end;
