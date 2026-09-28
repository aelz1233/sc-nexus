#ifndef AppVersion
  #define AppVersion "0.2.12"
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
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"; Flags: unchecked
Name: "restartafterupdate"; Description: "Запустить SC NEXUS после обновления"; Flags: unchecked

[Files]
Source: "{#PublishDir}\SCNexus.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\SC NEXUS"; Filename: "{app}\SCNexus.exe"
Name: "{autodesktop}\SC NEXUS"; Filename: "{app}\SCNexus.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SCNexus.exe"; Description: "Запустить SC NEXUS"; Flags: nowait postinstall skipifsilent
Filename: "{app}\SCNexus.exe"; Flags: nowait skipifdoesntexist; Tasks: restartafterupdate

; User data lives in %LOCALAPPDATA%\SCNexus, outside {app}.
; Never delete that directory during upgrade or uninstall.
