; 智能客服助手安装脚本（Inno Setup 6）
; 修改 AppVersion 后，按 Ctrl+F9 即可生成新的安装包。

#define AppName "智能客服助手"
#define AppVersion "1.0.0"
#define AppPublisher ""
#define AppExeName "Bot.exe"

[Setup]
AppId={{8D259CCB-75FD-4EA5-A7CC-84AC92A4BB87}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\QianNiuBot
DefaultGroupName={#AppName}
OutputDir=..\release
OutputBaseFilename=智能客服助手-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\src\Bot\Asset\branding\qianNiu-bot.ico

[Languages]
; 简体中文翻译文件与本脚本一同保存，避免依赖本机 Inno Setup 的语言目录。
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Files]
; 保留配置、日志配置、注入脚本和全部运行时 DLL；只排除调试符号。
Source: "..\src\Bin\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项："

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "立即启动 {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNet48OrLater(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
    and (Release >= 528040);
end;

function InitializeSetup(): Boolean;
begin
  Result := IsDotNet48OrLater();
  if not Result then
  begin
    MsgBox('此程序需要 .NET Framework 4.8 或更高版本，请先安装后再运行安装包。', mbError, MB_OK);
  end;
end;
