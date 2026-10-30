; Instalador do Gerenciador de Carteira (versão Windows), Inno Setup 6.
; Não rodar direto: o publicar.ps1 publica o app e chama o ISCC passando a versão e a pasta publicada.
;
; AppId NUNCA muda entre versões: é por ele que o instalador reconhece uma instalação existente e atualiza por cima.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef PastaPublicacao
  #define PastaPublicacao "..\artifacts\publish\desktop"
#endif
#ifndef PastaSaida
  #define PastaSaida "..\artifacts"
#endif

#define MyAppName "Gerenciador de Carteira"
#define MyAppExeName "GerenciadorDeCarteira.Desktop.exe"

[Setup]
AppId={{4BDBC126-E71B-49EA-AF6B-157909CE6DAF}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppName}
VersionInfoVersion={#MyAppVersion}
; Por usuário, sem administrador: {autopf} vira %LOCALAPPDATA%\Programs
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#MyAppName}
DisableDirPage=yes
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
SetupIconFile=..\GerenciadorDeCarteira\GerenciadorDeCarteira.Desktop\app.ico
OutputDir={#PastaSaida}
OutputBaseFilename=GerenciadorDeCarteira-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Mesmo Mutex que o app usa para ter uma janela só: pede para fechar o app antes de atualizar ou desinstalar
AppMutex=GerenciadorDeCarteira.Desktop
CloseApplications=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PastaPublicacao}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Instalando o Microsoft Edge WebView2..."; Check: not WebView2Instalado
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Só o cache do navegador embutido (e as preferências de tela). Os dados (banco, logs) ficam em
; %LOCALAPPDATA%\GerenciadorDeCarteira e NÃO são apagados.
Type: filesandordirs; Name: "{localappdata}\GerenciadorDeCarteira.Desktop.WebView2"

[Code]
// Detecção recomendada pela Microsoft para apps não empacotados: só instala o WebView2 se ele ainda não existir
function LerVersaoWebView2(RootKey: Integer; SubKeyName: String; var Versao: String): Boolean;
begin
  Result := RegQueryStringValue(RootKey, SubKeyName, 'pv', Versao) and (Versao <> '') and (Versao <> '0.0.0.0');
end;

function WebView2Instalado: Boolean;
var
  Versao: String;
  ChaveCliente: String;
begin
  ChaveCliente := 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result :=
    LerVersaoWebView2(HKLM64, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', Versao) or
    LerVersaoWebView2(HKLM64, ChaveCliente, Versao) or
    LerVersaoWebView2(HKCU, ChaveCliente, Versao);
end;
