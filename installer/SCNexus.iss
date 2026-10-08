#ifndef AppVersion
  #define AppVersion "0.6.2"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{C6417C6F-69DF-4396-A4AD-4B6114D0EE53}
AppName=SC NEXUS
AppVersion={#AppVersion}
AppPublisher=SC NEXUS
AppPublisherURL=https://github.com/aelz1233/sc-nexus
DefaultDirName={localappdata}\Programs\SC NEXUS
DisableDirPage=no
DefaultGroupName=SC NEXUS
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=SCNexus-Setup-{#AppVersion}-win-x64
SetupIconFile=..\SCNexus\Assets\sc-nexus.ico
UninstallDisplayIcon={app}\SCNexus.exe
WizardStyle=modern
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.DesktopIcon=Create a desktop shortcut
russian.DesktopIcon=Создать ярлык на рабочем столе
english.Shortcuts=Shortcuts:
russian.Shortcuts=Ярлыки:
english.RestartAfterUpdate=Start SC NEXUS after the update
russian.RestartAfterUpdate=Запустить SC NEXUS после обновления
english.LaunchApp=Start SC NEXUS
russian.LaunchApp=Запустить SC NEXUS

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:Shortcuts}"; Flags: unchecked
Name: "restartafterupdate"; Description: "{cm:RestartAfterUpdate}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\SCNexus.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\SC NEXUS"; Filename: "{app}\SCNexus.exe"
Name: "{autodesktop}\SC NEXUS"; Filename: "{app}\SCNexus.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\SCNexus"; ValueType: string; ValueName: "SetupLanguage"; ValueData: "{language}"

[Run]
Filename: "{app}\SCNexus.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\SCNexus.exe"; Flags: nowait skipifdoesntexist; Tasks: restartafterupdate

; User data lives in %LOCALAPPDATA%\SCNexus, outside {app}.
; Never delete that directory during upgrade or uninstall.
