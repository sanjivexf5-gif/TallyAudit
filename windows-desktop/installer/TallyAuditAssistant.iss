; =====================================================================
; Tally Audit Assistant - Inno Setup Script
; Production Windows Installer for Self-Contained .NET 8 x64 Desktop Application
; =====================================================================

#define MyAppName "Tally Audit Assistant"
#define MyAppVersion "1.0.0"
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
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Self-contained published binaries
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Custom Pascal Scripting for Safe User-Data Preservation on Uninstall
function InitializeUninstall(): Boolean;
begin
  Result := True;
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
