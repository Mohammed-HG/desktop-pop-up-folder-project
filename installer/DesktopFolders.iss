#ifndef PublishDir
  #define PublishDir "..\artifacts\DesktopFolders-win-x64"
#endif

[Setup]
AppId={{ECF0B355-85BB-43AD-BA8C-00F1337DFE65}
AppName=Desktop Pop-up folder
AppVersion=1.3
DefaultDirName={localappdata}\Programs\Desktop Folders
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19044
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\DesktopFolders.exe
SetupIconFile=..\src\DesktopFolders\Assets\folder.ico
OutputBaseFilename=DesktopFolders-Setup-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Desktop Pop-up folder"; Filename: "{app}\DesktopFolders.exe"; WorkingDir: "{app}"

[InstallDelete]
Type: files; Name: "{userprograms}\Desktop Folders.lnk"

[Registry]
Root: HKCU; Subkey: "Software\Classes\DesktopBackground\Shell\DesktopFolders.New"; ValueType: string; ValueData: "New Desktop Folder..."; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\DesktopBackground\Shell\DesktopFolders.New"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\DesktopFolders.exe"",0"
Root: HKCU; Subkey: "Software\Classes\DesktopBackground\Shell\DesktopFolders.New\command"; ValueType: string; ValueData: """{app}\DesktopFolders.exe"" --new-folder"

[Run]
Filename: "{app}\DesktopFolders.exe"; Description: "Open Desktop Pop-up folder"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\DesktopFolders.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "DesktopFoldersCleanup"
