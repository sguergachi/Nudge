; Nudge Windows Installer — Inno Setup
; Run from repo root: iscc "assets\windows\setup.iss"
; Override version: iscc /DMyAppVersion=2.0.0 "assets\windows\setup.iss"
; Requires: dist\win-x64\ from build.ps1 -Platform

#ifndef MyAppVersion
#define MyAppVersion "1.5.0"
#endif
#define MyAppName "Nudge"
#define MyAppPublisher "Nudge"
#define MyAppURL "https://github.com/sguergachi/Nudge"
#define MyAppExeName "nudge-tray.exe"

[Setup]
AppId={{C9E4B8D1-5A3F-4E7B-9C6D-2F8A1B4E3C7D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\..\..\dist
OutputBaseFilename=Nudge-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayIcon={app}\nudge-tray.exe
DisableDirPage=auto
DisableProgramGroupPage=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "..\..\..\dist\win-x64\nudge-tray.exe";     DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\..\dist\win-x64\nudge.exe";          DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\..\dist\win-x64\nudge-notify.exe";   DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\..\dist\win-x64\*.dll";              DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\..\..\dist\win-x64\*.json";             DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\..\..\dist\win-x64\runtimes\*";         DestDir: "{app}\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\..\..\model_inference.py";              DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\..\train_model.py";                  DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\..\background_trainer.py";           DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\..\requirements-cpu.txt";             DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\..\requirements.txt";                 DestDir: "{app}"; Flags: ignoreversion
Source: "nudge.ico";                              DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}";            Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\nudge.ico"
Name: "{group}\Nudge Daemon";             Filename: "{app}\nudge.exe"; WorkingDir: "{app}"
Name: "{group}\Respond YES";              Filename: "{app}\nudge-notify.exe"; Parameters: "YES"; WorkingDir: "{app}"
Name: "{group}\Respond NO";               Filename: "{app}\nudge-notify.exe"; Parameters: "NO"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}";   Filename: "{uninstallexe}"; IconFilename: "{app}\nudge.ico"
Name: "{autodesktop}\{#MyAppName}";       Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\nudge.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: postinstall nowait skipifsilent shellexec

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /f /im nudge-tray.exe /im nudge.exe /im nudge-notify.exe 2>nul"; Flags: runhidden
