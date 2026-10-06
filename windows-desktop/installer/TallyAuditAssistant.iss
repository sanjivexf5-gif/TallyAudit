; =====================================================================
; Tally Audit Assistant - Inno Setup Script
; Production Windows Installer for Self-Contained .NET 8 x64 Desktop Application
; =====================================================================

#define MyAppName "Tally Audit Assistant"
#define MyAppVersion "1.0.42"
#define MyAppPublisher "Tally Audit Assistant Systems"
#define MyAppURL "https://github.com/sanjivexf5-gif/TallyAudit"
#define MyAppExeName "TallyAuditAssistant.App.exe"

[Setup]
; Unique application GUID for install/upgrade identification
AppId={{E83B1A80-7189-4D62-8E3A-9F838BC4A102}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Application in-use / safe replacement handling
CloseApplications=force
RestartApplications=no
CloseApplicationsFilter=*.exe,*.dll
; Architecture configuration: Windows 64-bit strictly
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\installer_output
OutputBaseFilename=TallyAuditAssistant-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\Assets\TallyAuditAssistant.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Self-contained published binaries
Source: "..\..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\src\TallyAuditAssistant.App\Assets\TallyAuditAssistant.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\TallyAuditAssistant.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; IconFilename: "{app}\Assets\TallyAuditAssistant.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\TallyAuditAssistant.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Custom Pascal Scripting for Safe Process Termination and User-Data Preservation

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  // Pre-emptively request graceful termination of running TallyAuditAssistant.App.exe if active
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM TallyAuditAssistant.App.exe >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Attempts: Integer;
  ProcessExited: Boolean;
begin
  Result := '';
  NeedsRestart := False;

  // 1. Request graceful exit of running TallyAuditAssistant.App.exe
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM TallyAuditAssistant.App.exe >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);

  // 2. If still active after timeout, forcefully terminate ONLY TallyAuditAssistant.App.exe
  Attempts := 0;
  ProcessExited := False;
  while (Attempts < 5) and (not ProcessExited) do
  begin
    Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq TallyAuditAssistant.App.exe" 2>&1 | find /I "TallyAuditAssistant.App.exe" >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if ResultCode = 0 then
    begin
      // Process is still running: terminate ONLY TallyAuditAssistant.App.exe (never TallyPrime or shell)
      Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM TallyAuditAssistant.App.exe /T >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Sleep(1000);
      Attempts := Attempts + 1;
    end
    else
    begin
      ProcessExited := True;
    end;
  end;

  // 3. Final verification: ensure process is completely exited before file replacement begins
  Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq TallyAuditAssistant.App.exe" 2>&1 | find /I "TallyAuditAssistant.App.exe" >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode = 0 then
  begin
    Result := 'Tally Audit Assistant is currently running and could not be automatically closed.' + #13#10 +
              'Please close Tally Audit Assistant and click Retry.';
  end;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
  Attempts: Integer;
begin
  Result := True;
  // On uninstall, close running instance if active
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM TallyAuditAssistant.App.exe >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
  Attempts := 0;
  while Attempts < 3 do
  begin
    Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM TallyAuditAssistant.App.exe /T >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
    Attempts := Attempts + 1;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
  KeepData: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\TallyAuditAssistant');
    if DirExists(DataDir) then
    begin
      KeepData := MsgBox(
        'Do you want to PRESERVE your local SQLite databases, audit working papers, and evidence documents?' + #13#10 + #13#10 +
        'Select YES to keep your audit databases safely in ' + DataDir + '.' + #13#10 +
        'Select NO if you wish to permanently delete all local audit records.',
        mbConfirmation, MB_YESNO);

      if KeepData = IDNO then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;

