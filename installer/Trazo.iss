; Instalador de Trazo (Inno Setup 6). Compilar con:
;   ISCC.exe /DAppVersion=1.3.0 /DArch=x64 /DSourceDir=..\artifacts\publish\win-x64 installer\Trazo.iss
#ifndef AppVersion
  #define AppVersion "1.3.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-" + Arch
#endif

[Setup]
AppId={{6F3B8B2E-9C7A-4E21-9A0B-7A1D2C3E4F50}
AppName=Trazo
AppVersion={#AppVersion}
AppVerName=Trazo {#AppVersion}
AppPublisher=Mauro Gatti
AppPublisherURL=https://github.com/M4ur0-Gatt1/trazo
AppSupportURL=https://github.com/M4ur0-Gatt1/trazo/issues
DefaultDirName={autopf}\Trazo
DefaultGroupName=Trazo
DisableProgramGroupPage=yes
; instalación por usuario: no necesita permisos de administrador
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.19041
OutputDir=..\artifacts\installer
OutputBaseFilename=Trazo-{#AppVersion}-{#Arch}-setup
SetupIconFile=..\src\Trazo.App\Assets\Trazo.ico
UninstallDisplayIcon={app}\Trazo.exe
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "association"; Description: "Abrir los archivos .trazo con Trazo"; GroupDescription: "Asociaciones de archivo:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Trazo"; Filename: "{app}\Trazo.exe"
Name: "{autodesktop}\Trazo"; Filename: "{app}\Trazo.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.trazo"; ValueType: string; ValueName: ""; ValueData: "Trazo.Project"; Flags: uninsdeletevalue; Tasks: association
Root: HKA; Subkey: "Software\Classes\Trazo.Project"; ValueType: string; ValueName: ""; ValueData: "Proyecto de Trazo"; Flags: uninsdeletekey; Tasks: association
Root: HKA; Subkey: "Software\Classes\Trazo.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Trazo.exe,0"; Tasks: association
Root: HKA; Subkey: "Software\Classes\Trazo.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Trazo.exe"" ""%1"""; Tasks: association

[Run]
Filename: "{app}\Trazo.exe"; Description: "{cm:LaunchProgram,Trazo}"; Flags: nowait postinstall skipifsilent
