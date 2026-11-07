; =====================================================================
; Inno Setup Script for MDM (My Download Manager)
; =====================================================================

#define MyAppName "MDM"
#define MyAppFullName "My Download Manager"
#define MyAppPublisher "MDM Core Team"
#define MyAppURL "https://github.com/myownidm/mdm"
#define MyAppExeName "Mdm.exe"

[Setup]
; App Identity
AppId={{E57D5C14-2C62-4C78-9B2F-6A8109F21840}
AppName={#MyAppName}
AppVerName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Destination Directories
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes

; Output Configuration
OutputDir=SetupOutput
OutputBaseFilename=MDM_Setup
SetupIconFile=Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes

; Privileges & Modern Styling
PrivilegesRequiredOverridesAllowed=dialog commandline
PrivilegesRequired=lowest
WizardStyle=modern
WizardSizePercent=115,115
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=no
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppFullName} Installer
VersionInfoProductName={#MyAppFullName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Jalankan MDM secara otomatis saat Windows dinyalakan (Start on Boot)"; GroupDescription: "Pengaturan Startup:"

[Files]
; Main publish binaries
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Note: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Tasks: desktopicon

[Registry]
; Windows autostart on logon (if autostart task selected)
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MDM"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Close running instance of MDM before installation
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  // Kill running MDM if currently running
  Exec('taskkill.exe', '/f /im Mdm.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Close running instance before uninstallation
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  Exec('taskkill.exe', '/f /im Mdm.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

