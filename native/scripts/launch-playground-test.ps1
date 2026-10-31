param(
    [ValidateSet('Trial','Demo')][string]$Mode='Trial',
    [string]$DataDirectory
)
$ErrorActionPreference='Stop'
$package=[IO.Path]::GetFullPath($PSScriptRoot)
$exe=Join-Path $package 'TheOrder.WinUI.exe'
if(!(Test-Path -LiteralPath $exe)){throw '请从完整的 Playground 候选目录运行此入口。'}
if([IO.Path]::GetPathRoot($package) -ne 'D:\'){throw '为减少 C 盘占用，请把完整候选目录放在 D 盘。'}
if(!$DataDirectory){$DataDirectory=Join-Path (Split-Path $package -Parent) ('profiles\Playground-'+$Mode)}
$profile=[IO.Path]::GetFullPath($DataDirectory)
if([IO.Path]::GetPathRoot($profile) -ne 'D:\' -or $profile -eq 'D:\'){throw '请指定 D 盘中的独立 Test 资料目录。'}
function Assert-PlainDirectoryChain([string]$path){
    for($folder=[IO.DirectoryInfo]::new($path);$null -ne $folder;$folder=$folder.Parent){
        try{if(([IO.File]::GetAttributes($folder.FullName) -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw '候选与资料目录不能使用目录链接。'}}catch [IO.FileNotFoundException]{}catch [IO.DirectoryNotFoundException]{}
    }
}
foreach($path in @($package,$profile)){Assert-PlainDirectoryChain $path}
$profileManifest=Join-Path $profile 'native-profile.json'
if(Test-Path -LiteralPath $profileManifest){
    $identity=Get-Content -LiteralPath $profileManifest -Raw -Encoding UTF8|ConvertFrom-Json
    if($identity.ProfileKind -ne 'Test'){throw '此入口只能使用 Test 资料，不会打开或转换正式书架。'}
}
$manifestPath=Join-Path $package 'files.json'
if(!(Test-Path -LiteralPath $manifestPath)){throw '候选完整性清单缺失；没有打开资料。'}
$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json
if($manifest.schema -ne 1 -or $manifest.files -isnot [array] -or $manifest.files.Count -lt 5 -or $manifest.files.Count -gt 20000){throw '候选完整性清单格式错误；没有打开资料。'}
$listed=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($item in $manifest.files){
    if($item.path -isnot [string] -or [string]::IsNullOrWhiteSpace($item.path) -or [IO.Path]::IsPathRooted($item.path) -or $item.path.Contains(':') -or
        $item.sha256 -notmatch '^[0-9A-Fa-f]{64}$' -or $null -eq $item.bytes -or $item.bytes -lt 0 -or !$listed.Add($item.path)){throw '候选完整性清单条目错误；没有打开资料。'}
    $path=[IO.Path]::GetFullPath((Join-Path $package $item.path))
    if(!$path.StartsWith($package+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $path) -or
        ([IO.File]::GetAttributes($path) -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or (Get-Item -LiteralPath $path).Length -ne $item.bytes -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $item.sha256){throw ('候选文件缺失或变化：'+$item.path+'。请恢复完整目录；没有打开资料。')}
    Assert-PlainDirectoryChain ([IO.Path]::GetDirectoryName($path))
}
foreach($required in @('TheOrder.WinUI.exe','TheOrder.WinUI.dll','TheOrder.WinUI.pri','Start-Playground.ps1','Start-Playground.cmd')){
    if(!$listed.Contains($required)){throw '候选完整性清单缺少必要入口；没有打开资料。'}
}
$temporary=Join-Path $package '.runtime-temp'
Assert-PlainDirectoryChain $temporary
[IO.Directory]::CreateDirectory($temporary)|Out-Null
$start=[Diagnostics.ProcessStartInfo]::new($exe)
$start.UseShellExecute=$false; $start.WorkingDirectory=$package
foreach($name in @($start.EnvironmentVariables.Keys)){
    if($name.StartsWith('THE_ORDER_NATIVE_') -or $name -in @('KGM_DATA_DIR','KGM_TEST_EXECUTABLE','DOTNET_ROOT','DOTNET_ROOT_X64')){[void]$start.EnvironmentVariables.Remove($name)}
}
$values=@{TEMP=$temporary;TMP=$temporary;THE_ORDER_NATIVE_DATA_DIR=$profile;THE_ORDER_NATIVE_PROFILE_KIND='Test';THE_ORDER_NATIVE_FIXTURE_PROFILE='0';THE_ORDER_NATIVE_DIAGNOSTICS='0';THE_ORDER_NATIVE_VISUAL_TEST='0';THE_ORDER_NATIVE_PLAYGROUND_OPEN='1';THE_ORDER_NATIVE_PLAYGROUND_CLEAN='1';THE_ORDER_NATIVE_PLAYGROUND_FIXTURE='0'}
if($Mode -eq 'Demo'){
    $control=Join-Path (Split-Path $profile -Parent) 'demo-control'
    Assert-PlainDirectoryChain $control
    [IO.Directory]::CreateDirectory($control)|Out-Null
    $values.THE_ORDER_NATIVE_DIAGNOSTICS='1'; $values.THE_ORDER_NATIVE_VISUAL_TEST='1'
    $values.THE_ORDER_NATIVE_VISUAL_CONTROL=$control; $values.THE_ORDER_NATIVE_PLAYGROUND_FIXTURE='1';$values.THE_ORDER_NATIVE_ONLINE_FIXTURE='1'
}
foreach($name in $values.Keys){$start.EnvironmentVariables[$name]=[string]$values[$name]}
[void][Diagnostics.Process]::Start($start)
