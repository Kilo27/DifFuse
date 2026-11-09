; Inno Setup script for the DifFuse installer.
;
; Build it with installer\build-installer.ps1, which publishes the app and passes in
; AppVersion and SourceDir. The result is a one-click setup: no wizard pages, no admin
; prompt. It installs for the current user, adds Start menu and desktop shortcuts,
; registers DifFuse under Settings > Apps > Installed apps, and starts the app.
;
; Uninstalling removes the program only. Presets in Documents\DifFuse and settings in
; %APPDATA%\DifFuse are left alone.

#ifndef AppVersion
  #error AppVersion is not defined. Build with installer\build-installer.ps1.
#endif
#ifndef SourceDir
  #error SourceDir is not defined. Build with installer\build-installer.ps1.
#endif

#define AppName "DifFuse"
#define AppExe "DifFuse.exe"

[Setup]
; AppId identifies DifFuse to Windows across versions. Never change it, or upgrades
; will install side by side instead of replacing the old version.
AppId={{E95C161C-0064-4E93-9461-B30DBD8F7A63}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Kilo27
AppPublisherURL=https://github.com/Kilo27/DifFuse
AppComments=Editor for Fender Mustang amps
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup

; Per-user install: {autopf} is %LOCALAPPDATA%\Programs, so no admin rights are needed.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}

; The app is a self-contained x64 build. x64compatible also covers Windows 11 on Arm,
; which runs it under emulation.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

; One click: skip every wizard page. Setup shows a progress bar and then starts DifFuse.
; If DifFuse is running during an upgrade, Setup still asks to close it first.
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
ShowLanguageDialog=no
CloseApplications=yes
RestartApplications=no

WizardStyle=modern
SetupIconFile=..\src\DifFuse.App\Assets\DifFuse.ico
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Editor for Fender Mustang amps"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Editor for Fender Mustang amps"

[Registry]
; A hand-made install from older README instructions could add its own "Installed apps"
; entry under this key. Remove it so DifFuse isn't listed twice.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppName}"; ValueType: none; Flags: deletekey

[Run]
; Start DifFuse when Setup finishes (skipped for /SILENT and /VERYSILENT installs).
Filename: "{app}\{#AppExe}"; Flags: nowait skipifsilent

[Code]
// DisableReadyPage is ignored when no earlier page was shown, so with every other page
// disabled Setup would still stop on "Ready to Install" and wait for a click on Install.
// Press Install for the user. The click is posted rather than made directly because this
// runs before the wizard window is on screen; the posted click lands once it is.
const
  BM_CLICK = $00F5;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpReady) and not WizardSilent then
    PostMessage(WizardForm.NextButton.Handle, BM_CLICK, 0, 0);
end;
