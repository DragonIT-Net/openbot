; 智能客服助手安装脚本（Inno Setup 6）
; 修改 AppVersion 后，按 Ctrl+F9 即可生成新的安装包。

; AppVersion 是全项目唯一手动维护的版本号，发布新版本只改这一行——
; 编译 Bot.csproj 时会自动同步换算到 AssemblyInfo.cs 和 StartUp/Params.cs（见 tools/SyncVersion.ps1）。
#define AppName "智能客服助手"
#define AppVersion "1.0.3"
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
UninstallDisplayIcon={app}\Bin\{#AppExeName}
SetupIconFile=..\src\Bot\Asset\branding\qianNiu-bot.ico
; 静默自动更新依赖这两项：装的时候如果发现 Bot.exe/DLL 被占用，自动关闭旧进程；
; 装完之后用原来的方式（同一个可执行文件+参数）自动重新拉起，不用自己写等待/重启逻辑。见 ADR 0005。
CloseApplications=force
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=yes

[Languages]
; 简体中文翻译文件与本脚本一同保存，避免依赖本机 Inno Setup 的语言目录。
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Files]
; 保留配置、日志配置、注入脚本和全部运行时 DLL；只排除调试符号。
; 注意：必须装进 {app}\Bin 子目录，不能直接摊在 {app} 根目录——
; 程序的运行日志和 data\ 目录是按"exe 所在目录的上一级"定位的（见 PathEx.GetParentSiblingDir），
; 如果 exe 直接放在 {app} 根目录，日志/数据会被写到 {app} 的上一级（比如 C:\Program Files\）去。
Source: "..\src\Bot\bin\x64\Release\*"; DestDir: "{app}\Bin"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项："

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\Bin\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Bin\{#AppExeName}"; Tasks: desktopicon

[Run]
; Must run during /VERYSILENT automatic updates as well.
Filename: "{app}\Bin\{#AppExeName}"; Flags: nowait

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
