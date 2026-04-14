[Setup]
AppName=PocketMC
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
AppVersion={#AppVersion}
DefaultDirName={autopf}\PocketMC Desktop
DefaultGroupName=PocketMC
OutputDir=ReleaseOutput
OutputBaseFilename=PocketMC_Setup
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
DisableDirPage=no
PrivilegesRequired=lowest
SetupIconFile=PocketMC.Desktop\icon.ico
UninstallDisplayIcon={app}\PocketMC.Desktop.exe

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "publish_output\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\PocketMC Desktop"; Filename: "{app}\PocketMC.Desktop.exe"
Name: "{group}\Uninstall PocketMC"; Filename: "{uninstallexe}"
Name: "{autodesktop}\PocketMC Desktop"; Filename: "{app}\PocketMC.Desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PocketMC.Desktop.exe"; Description: "Launch PocketMC Desktop"; Flags: nowait postinstall skipifsilent
