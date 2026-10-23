; Inno Setup script - builds MetSaver-Setup.exe (per-user install, no admin rights needed).
; Build:  iscc installer\MetSaver.iss /DAppVersion=1.0.0 /DSourceExe=publish\MetSaver.exe

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\publish\MetSaver.exe"
#endif

[Setup]
AppId={{6F1B2C84-3A52-4D0E-9C57-5E7A0B1D2F43}
AppName=MetSaver
AppVersion={#AppVersion}
AppPublisher=MetSaver contributors
DefaultDirName={localappdata}\MetSaver
DefaultGroupName=MetSaver
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=MetSaver-Setup
Compression=lzma2
SolidCompression=yes
DisableProgramGroupPage=yes
UninstallDisplayName=MetSaver screensaver
WizardStyle=modern

[Tasks]
Name: "activate"; Description: "Make MetSaver my screensaver (starts after 3 minutes idle)"; Flags: checkedonce
Name: "seed"; Description: "Download a starter set of paintings now (about 30 MB)"; Flags: checkedonce

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "MetSaver.scr"; Flags: ignoreversion
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "MetSaver.exe"; Flags: ignoreversion

[Icons]
Name: "{group}\MetSaver settings"; Filename: "{app}\MetSaver.exe"; Parameters: "/c"

[Run]
Filename: "{app}\MetSaver.exe"; Parameters: "/install 180"; Flags: runhidden; Tasks: activate
Filename: "{app}\MetSaver.exe"; Parameters: "/seed"; Flags: runhidden; Tasks: seed; StatusMsg: "Downloading paintings from The Met..."
Filename: "{app}\MetSaver.exe"; Parameters: "/c"; Description: "Open MetSaver settings"; Flags: postinstall nowait skipifsilent

[UninstallRun]
; Put the user's previous screensaver back before the files are removed.
Filename: "{app}\MetSaver.exe"; Parameters: "/uninstall"; Flags: runhidden; RunOnceId: "RestoreScreensaver"
