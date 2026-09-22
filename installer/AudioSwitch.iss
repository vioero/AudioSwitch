[Setup]
AppId={{A7B2C4D8-9E1F-4A3B-8C5D-6E7F8A9B0C1D}}
AppName=AudioSwitch
; 版本号唯一源：src/AudioSwitch/AudioSwitch.csproj 的 <Version>
; 用 build.ps1 打包时会自动注入；手工编译则改这里的回退值
#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif
#ifndef AppVersionFull
  #define AppVersionFull "1.0.1.0"
#endif
AppVersion={#AppVersion}
AppPublisher=AudioSwitch
DefaultDirName={autopf}\AudioSwitch
DefaultGroupName=AudioSwitch
OutputDir=..\..\安装包
OutputBaseFilename=AudioSwitch-Setup
SetupIconFile=..\src\AudioSwitch\Assets\app.ico
UninstallDisplayName=AudioSwitch
UninstallDisplayIcon={app}\AudioSwitch.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersionFull}
VersionInfoDescription=Audio device quick switcher
VersionInfoProductName=AudioSwitch
VersionInfoProductVersion={#AppVersionFull}

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"
Name: "autostart"; Description: "开机自动启动 AudioSwitch"; GroupDescription: "其他选项:"

[Files]
; .NET 8 Desktop Runtime 安装包（提取到临时目录，安装后自动删除）
Source: "redist\windowsdesktop-runtime-8.0.30-win-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
; 主程序（先 dotnet publish 到 项目\publish\，再编译本脚本；成品输出到 安装包\）
Source: "..\publish\AudioSwitch.exe"; DestDir: "{app}"; Flags: ignoreversion

[Run]
; 步骤1：安装 .NET 8 Desktop Runtime（静默安装，显示进度）
Filename: "{tmp}\windowsdesktop-runtime-8.0.30-win-x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "正在安装 .NET 8 运行时..."; Flags: waituntilterminated skipifsilent runhidden
; 步骤2：启动 AudioSwitch
Filename: "{app}\AudioSwitch.exe"; Description: "立即启动 AudioSwitch"; Flags: nowait postinstall skipifsilent

[Icons]
Name: "{group}\AudioSwitch"; Filename: "{app}\AudioSwitch.exe"
Name: "{group}\卸载 AudioSwitch"; Filename: "{uninstallexe}"
Name: "{autodesktop}\AudioSwitch"; Filename: "{app}\AudioSwitch.exe"; Tasks: desktopicon

[Registry]
; 仅在用户勾选"开机自动启动"时写入 Run 项
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AudioSwitch"; ValueData: "\"{app}\AudioSwitch.exe\""; Tasks: autostart; Flags: uninsdeletevalue
