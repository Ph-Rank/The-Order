; SPDX-License-Identifier: AGPL-3.0-or-later
; Build only through scripts/package-native-installer.ps1, which checks every input.
#ifndef PayloadDirectory
  #error PayloadDirectory is required
#endif
#ifndef AddonDirectory
  #error AddonDirectory is required
#endif
#ifndef OutputDirectory
  #error OutputDirectory is required
#endif
#ifndef PayloadEntries
  #error Verified PayloadEntries is required
#endif
#ifndef PayloadChecks
  #error Verified PayloadChecks is required
#endif
#ifndef ReleaseVersion
  #define ReleaseVersion "1.1.0"
#endif
#ifndef ReleaseFileVersion
  #define ReleaseFileVersion "1.1.0.0"
#endif
#define ProductName "The Order"
#define SetupId "{{F7940B13-6814-42F7-AB71-69A9901D7DB3}"
#define SetupIdentity "TheOrder.Setup.F7940B13-6814-42F7-AB71-69A9901D7DB3"
#define AppUserId "dev.local.kungalmanager"
#define ProfileRoot "{localappdata}\TheOrder\Profiles\Production"

[Setup]
AppId={#SetupId}
AppName={#ProductName}
AppVersion={#ReleaseVersion}
AppVerName={#ProductName} {#ReleaseVersion}
AppPublisher=The Order Project
AppPublisherURL=https://github.com/Ph-Rank/The-Order
AppSupportURL=https://github.com/Ph-Rank/The-Order/issues
AppUpdatesURL=https://github.com/Ph-Rank/The-Order/releases
DefaultDirName={localappdata}\Programs\{#ProductName}
DefaultGroupName={#ProductName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.17763
DisableProgramGroupPage=yes
DisableDirPage=no
DirExistsWarning=no
AllowUNCPath=no
AllowNetworkDrive=no
OutputDir={#OutputDirectory}
OutputBaseFilename=The-Order-{#ReleaseVersion}-Setup-x64
SetupIconFile={#PayloadDirectory}\Assets\Brand.Light.ico
UninstallDisplayIcon={app}\TheOrder.exe
LicenseFile={#PayloadDirectory}\LICENSE-TheOrder.txt
WizardStyle=modern dynamic
Compression=lzma2/max
; Separate compression streams keep unchanged files reusable for differential downloads.
SolidCompression=no
DiskSpanning=no
CloseApplications=no
RestartApplications=no
SetupMutex={#SetupIdentity}
UninstallDisplayName={#ProductName}
UninstallLogMode=append
UsePreviousAppDir=yes
UsePreviousTasks=yes
VersionInfoVersion={#ReleaseFileVersion}
VersionInfoDescription={#ProductName} 安装程序
VersionInfoProductName={#ProductName}
VersionInfoProductVersion={#ReleaseFileVersion}
VersionInfoProductTextVersion={#ReleaseVersion}

[Languages]
Name: "zhHans"; MessagesFile: "languages\ChineseSimplified.isl"

[Messages]
#ifdef PublicBeta
WelcomeLabel2=将安装 The Order Beta 测试通道。%n%n本版本与正式通道共用程序入口，继续使用所选书架。测试功能仍可能调整；请先备份资料。可在设置中切回兼容的正式版。
#else
WelcomeLabel2=将安装 The Order 正式通道。%n%n升级或通道切换保留所选书架、设置和游玩记录。可在设置中选择 Beta 测试通道。卸载保留全部使用资料。
#endif
FinishedLabel=The Order 已安装完成。%n%n可从桌面或开始菜单打开，在设置中选择更新通道。需要完全退出时，请在系统托盘选择“退出”。
ConfirmUninstall=确定要卸载 The Order 吗？%n%n书架、个人记录、设置与备份会保留，游戏文件不会删除。

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
; Every expected payload/entry/license hash is compiled locally. Only changed/missing files are copied.
#include PayloadEntries
; Keep exactly one installer as the next differential base. Normal uninstall owns/removes it.
; This is a local copy of this Setup, never another download or a runnable patch instruction.
Source: "{srcexe}"; DestDir: "{app}"; DestName: "update-base.bin"; Flags: external ignoreversion

[Icons]
Name: "{group}\{#ProductName}"; Filename: "{app}\TheOrder.exe"; WorkingDir: "{app}"; IconFilename: "{code:ShortcutIcon}"; AppUserModelID: "{#AppUserId}"
Name: "{autodesktop}\{#ProductName}"; Filename: "{app}\TheOrder.exe"; WorkingDir: "{app}"; IconFilename: "{code:ShortcutIcon}"; AppUserModelID: "{#AppUserId}"; Tasks: desktopicon
Name: "{group}\卸载 {#ProductName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\TheOrder.exe"; Description: "打开 The Order"; Flags: nowait postinstall skipifsilent unchecked

[UninstallDelete]
; Only a routing choice in the program directory; all user profile data is retained.
Type: files; Name: "{app}\active-profile.json"

[Code]
function GetFileAttributesW(Name: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';
#include "PayloadFiles.iss"
#include PayloadChecks

function ShortcutIcon(Param: String): String;
var Light: Cardinal;
begin
  Light := 0;
  RegQueryDWordValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Themes\Personalize', 'SystemUsesLightTheme', Light);
  if Light <> 0 then Result := ExpandConstant('{app}\Assets\Brand.Light.ico')
  else Result := ExpandConstant('{app}\Assets\Brand.Dark.ico');
end;

function RunningError: String;
var
  Locator, Service, Processes, Process: Variant;
  ProcessIndex: Integer;
begin
  Result := '';
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE Name = ''TheOrder.WinUI.exe'' OR Name = ''TheOrder.MigrationTool.exe''');
    for ProcessIndex := 0 to Processes.Count - 1 do begin
      Process := Processes.ItemIndex(ProcessIndex);
      if VarIsNull(Process.ExecutablePath) then begin
        Result := '无法确认运行中的程序路径，未开始安装。'; Exit;
      end;
      if (CompareText(ExtractFileDir(Process.ExecutablePath), RemoveBackslashUnlessRoot(ExpandConstant('{app}'))) = 0) or
         (Pos(LowerCase(RemoveBackslashUnlessRoot(ExpandConstant('{app}')) + '\Tools\LibraryMigration\'), LowerCase(Process.ExecutablePath)) = 1) then begin
        Result := '这个 The Order 仍在运行。请先从其托盘正常退出，再安装或卸载。'; Exit;
      end;
    end;
  except
    Result := '无法确认 The Order 是否已退出，本次操作未开始。请稍后重试。';
  end;
end;

function PathsOverlap(Left, Right: String): Boolean;
begin
  Left := Lowercase(AddBackslash(RemoveBackslashUnlessRoot(ExpandFileName(Left))));
  Right := Lowercase(AddBackslash(RemoveBackslashUnlessRoot(ExpandFileName(Right))));
  Result := (Pos(Left, Right) = 1) or (Pos(Right, Left) = 1);
end;

function TargetError: String;
var
  Target, Parent: String;
  Attributes: Cardinal;
  FindRec: TFindRec;
  HasContents: Boolean;
begin
  Result := '';
  Target := RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{app}')));
  if (Length(Target) <= 3) or (Target[2] <> ':') or
     PathsOverlap(Target, ExpandConstant('{localappdata}\TheOrder')) or
     PathsOverlap(Target, ExpandConstant('{localappdata}\TheOrder-RC')) or
     PathsOverlap(Target, ExpandConstant('{localappdata}\TheOrder-Beta')) or
     PathsOverlap(Target, ExpandConstant('{userappdata}\KunGalManager')) then begin
    Result := '请使用独立的程序目录，不能安装到资料目录或磁盘根目录。';
    Exit;
  end;
  Parent := Target;
  while Parent <> '' do begin
    Attributes := GetFileAttributesW(Parent);
    if (Attributes <> $FFFFFFFF) and ((Attributes and $400) <> 0) then begin
      Result := '安装目录包含链接或目录联接。请改用普通的本地文件夹。';
      Exit;
    end;
    if Parent = ExtractFileDir(Parent) then Break;
    Parent := ExtractFileDir(Parent);
  end;
  if not PayloadPathsSafe then begin
    Result := '安装目标包含链接文件或目录，本次安装未开始。请检查安装目录。';
    Exit;
  end;
  HasContents := False;
  Attributes := GetFileAttributesW(AddBackslash(Target) + 'update-base.bin');
  if (Attributes <> $FFFFFFFF) and ((Attributes and $400) <> 0) then begin
    Result := '更新缓存是链接文件。请先检查安装目录，本次安装未开始。';
    Exit;
  end;
  if FindFirst(AddBackslash(Target) + '*', FindRec) then begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then HasContents := True;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
  if HasContents and (GetIniString('Installer', 'Identity', '',
      AddBackslash(Target) + 'installer-identity.ini') <> '{#SetupIdentity}') then
    Result := '这个目录已有其他文件。请选择新的程序目录，已有便携版无需删除，书架资料会继续保留。';
end;

function InitializeSetup: Boolean;
begin
  // {app} is not initialized here. Check the selected directory's processes in
  // PrepareToInstall, after directory selection and before any payload writes.
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := RunningError;
  if Result = '' then Result := TargetError;
end;

// Channel switches and uninstall retain all profile namespaces and the routing choice.
function InitializeUninstall: Boolean;
var Error: String;
begin
  Error := RunningError;
  Result := Error = '';
  if not Result then begin
    Log(Error);
    if not UninstallSilent then MsgBox(Error, mbError, MB_OK);
  end;
end;
