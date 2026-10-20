; Instalador do MACH1 custom app (Inno Setup 6).
; Antes de compilar: rode ..\publicar.cmd (gera ..\dist) e coloque em deps\ o PawnIO_setup.exe
; (github.com/namazso/PawnIO.Setup), o ffmpeg.exe com a licença dele (gyan.dev, build essentials) e o
; OpenRGB_1.0_Windows_64_81bbe18.msi com o OpenRGB-LICENSE.txt (release_1.0 do OpenRGB).
; Compilar: ISCC.exe setup.iss  ->  out\MACH1-custom-app-setup-<versão>.exe
; Com /DTestBuild sai uma variante com outro nome e outro AppId, para testar o instalador sem
; tocar em uma instalação de verdade.

#define AppVersion "1.0.6"
#ifdef TestBuild
  #define AppName "MACH1 custom app (teste)"
  #define AppGuid "0B7E5D51-9C1F-4E0A-8F3B-6A2D4C9E7F10"
  #define OutputName "teste-do-instalador"
#else
  #define AppName "MACH1 custom app"
  #define AppGuid "6F3C2B9A-41D7-4C0E-9B5A-2E8D7A1F5C34"
  #define OutputName "MACH1-custom-app-setup-" + AppVersion
#endif
#define OpenRgbMsi "OpenRGB_1.0_Windows_64_81bbe18.msi"
#define AppExe "Mach1CustomApp.exe"
#define TaskName "Mach1CustomApp"

[Setup]
AppId={{{#AppGuid}}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=out
OutputBaseFilename={#OutputName}
SetupIconFile=..\app\Assets\avalonia-logo.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; O app aberto segura os arquivos e as ventoinhas: o instalador pede para fechá-lo antes.
AppMutex=Mach1CustomApp.Instance
CloseApplications=no

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar um atalho na área de trabalho"; Flags: unchecked
Name: "autostart"; Description: "Abrir junto com o Windows, na bandeja"; Flags: unchecked
; Só aparece se o OpenRGB ainda não estiver na máquina.
Name: "openrgb"; Description: "Instalar o OpenRGB 1.0, usado pela aba LEDs para controlar os LEDs RGB do PC"; Check: OpenRgbNeeded

[Files]
; O app com o .NET junto (não precisa instalar o .NET na máquina).
Source: "..\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; ffmpeg ao lado do app: é o que decodifica vídeo de fundo na tela.
Source: "deps\ffmpeg.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "deps\ffmpeg-LICENSE.txt"; DestDir: "{app}\licencas"; Flags: ignoreversion
Source: "..\LICENCAS.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
; Driver PawnIO: só é executado se ainda não estiver instalado (ver PawnIONeeded).
Source: "deps\PawnIO_setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
; OpenRGB: o instalador oficial, sem alteração. Ele registra um serviço do Windows que já sobe com o
; servidor SDK ligado, que é por onde a aba LEDs manda as cores.
Source: "deps\{#OpenRgbMsi}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Tasks: openrgb
Source: "deps\OpenRGB-LICENSE.txt"; DestDir: "{app}\licencas"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\PawnIO_setup.exe"; Parameters: "-install -silent"; StatusMsg: "Instalando o driver PawnIO..."; Check: PawnIONeeded; Flags: waituntilterminated
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\{#OpenRgbMsi}"" /qn /norestart"; StatusMsg: "Instalando o OpenRGB..."; Tasks: openrgb; Flags: waituntilterminated
; A mesma tarefa que o app cria pela aba Geral: abre na bandeja a cada logon, já elevado.
Filename: "schtasks.exe"; Parameters: "/Create /F /TN {#TaskName} /SC ONLOGON /RL HIGHEST /DELAY 0000:15 /TR ""\""{app}\{#AppExe}\"" --tray"""; Flags: runhidden; Tasks: autostart
Filename: "{app}\{#AppExe}"; Description: "Abrir o {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Encerra do jeito normal, para devolver ventoinhas e bomba à placa-mãe.
Filename: "{app}\{#AppExe}"; Parameters: "--quit"; Flags: runhidden; RunOnceId: "QuitApp"
; Remove a tarefa de logon só se ela apontar para esta instalação.
Filename: "{cmd}"; Parameters: "/C schtasks /Query /TN {#TaskName} /FO LIST /V | findstr /I /C:""{app}"" && schtasks /Delete /F /TN {#TaskName}"; Flags: runhidden; RunOnceId: "RemoveTask"

[Code]
function PawnIONeeded: Boolean;
begin
  Result := not RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;

{ O OpenRGB já está na máquina se o programa ou o serviço dele existem; nesse caso não é oferecido. }
function OpenRgbNeeded: Boolean;
begin
  Result := not FileExists(ExpandConstant('{commonpf64}\OpenRGB\OpenRGB.exe')) and
            not RegKeyExists(HKLM64, 'SYSTEM\CurrentControlSet\Services\OpenRGB');
end;

{ Ao atualizar por cima de uma instalação, fecha o app que estiver aberto antes de copiar. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Existing: String;
  ResultCode: Integer;
begin
  Result := '';
  Existing := ExpandConstant('{app}\{#AppExe}');
  if FileExists(Existing) then
  begin
    Exec(Existing, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(2500);
  end;
end;
