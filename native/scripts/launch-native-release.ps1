param([switch]$Test, [string]$DataDirectory, [switch]$ValidateOnly, [switch]$Wait)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1') -Force -ErrorAction Stop
# Compatible with Windows PowerShell 5.1. Validation never opens a Production profile.
$package = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$exe = Join-Path $package 'TheOrder.WinUI.exe'
$identity = Get-Content -LiteralPath (Join-Path $package 'release-identity.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$normalKind = 'Production' # The application resolves an explicit legacy-Beta library choice.
if (!$Test -and $DataDirectory) { throw 'A custom data directory is available only with -Test.' }
$profile = $null
if ($Test) {
    if (!$DataDirectory) { $DataDirectory = Join-Path $env:LOCALAPPDATA 'TheOrder-RC/Test' }
    if ($DataDirectory -notmatch '^[A-Za-z]:[\\/]' -or $DataDirectory.IndexOf(':', 2) -ge 0) { throw 'Use an absolute local Test directory.' }
    $profile = [IO.Path]::GetFullPath($DataDirectory).TrimEnd('\')
    foreach ($formalRoot in @((Join-Path $env:APPDATA 'KunGalManager'), (Join-Path $env:LOCALAPPDATA 'TheOrder/Profiles/Production'), (Join-Path $env:LOCALAPPDATA 'TheOrder/Profiles/Development'), (Join-Path $env:LOCALAPPDATA 'TheOrder-Beta/Profiles/Beta'))) {
        $formal = [IO.Path]::GetFullPath($formalRoot).TrimEnd('\')
        if ($profile.Equals($formal, [StringComparison]::OrdinalIgnoreCase) -or $profile.StartsWith($formal + '\', [StringComparison]::OrdinalIgnoreCase) -or $formal.StartsWith($profile + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'A formal profile or its parent/child cannot be used as Test.'
        }
    }
    for ($directory = [IO.DirectoryInfo]::new($profile); $directory; $directory = $directory.Parent) {
        try { if (([IO.File]::GetAttributes($directory.FullName) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'A linked profile path cannot be used as Test.' } }
        catch [IO.DirectoryNotFoundException] { }
        catch [IO.FileNotFoundException] { }
    }
    $profileManifest = Join-Path $profile 'native-profile.json'
    if (Test-Path -LiteralPath $profileManifest) {
        if (([IO.File]::GetAttributes($profileManifest) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'A linked profile manifest cannot be used as Test.' }
        $stored = Get-Content -LiteralPath $profileManifest -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($stored.ProfileKind -ne 'Test') { throw 'Existing profile is not Test; it will not be changed.' }
    }
}
$manifest = Get-Content -LiteralPath (Join-Path $package 'files.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.schema -ne 1 -or !$manifest.files -or @($manifest.files | Where-Object path -eq 'TheOrder.WinUI.exe').Count -ne 1) { throw 'Invalid package manifest.' }
$seen = @{}
foreach ($file in $manifest.files) {
    if (!$file.path -or [IO.Path]::IsPathRooted($file.path) -or $file.path.IndexOf(':') -ge 0) { throw 'Invalid package path.' }
    $path = [IO.Path]::GetFullPath((Join-Path $package $file.path))
    if (!$path.StartsWith($package + '\', [StringComparison]::OrdinalIgnoreCase) -or $seen.ContainsKey($path)) { throw 'Duplicate or escaped package path.' }
    $seen[$path] = $true
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $file.bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) {
        throw "Program file missing or changed: $($file.path). Extract the complete package again; no data was opened."
    }
}
if ($ValidateOnly) {
    [pscustomobject]@{version=[Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion;files=@($manifest.files).Count;validated=$true;profileKind=$(if($Test){'Test'}else{$normalKind});profileOpened=$false} | ConvertTo-Json
    return
}
$prior = @{}
Get-ChildItem Env: | Where-Object { $_.Name.StartsWith('THE_ORDER_NATIVE_', [StringComparison]::OrdinalIgnoreCase) -or $_.Name -in @('KGM_DATA_DIR','KGM_TEST_EXECUTABLE','DOTNET_ROOT','DOTNET_ROOT_X64') } | ForEach-Object { $prior[$_.Name] = $_.Value }
$values = @{THE_ORDER_NATIVE_PROFILE_KIND=$(if($Test){'Test'}else{$normalKind});THE_ORDER_NATIVE_FIXTURE_PROFILE='0';THE_ORDER_NATIVE_DIAGNOSTICS='0';THE_ORDER_NATIVE_VISUAL_TEST='0';THE_ORDER_NATIVE_ADVANCED_TEST='0'}
if ($Test) { $values.THE_ORDER_NATIVE_DATA_DIR = $profile }
$process = $null
try {
    foreach ($name in $prior.Keys) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
    foreach ($name in $values.Keys) { [Environment]::SetEnvironmentVariable($name, $values[$name], 'Process') }
    if ($Test) { $process = Start-Process -FilePath $exe -WorkingDirectory $package -WindowStyle Hidden -PassThru }
    else { $process = Start-Process -FilePath $exe -WorkingDirectory $package -ArgumentList '--allow-production-profile' -WindowStyle Hidden -PassThru }
    [pscustomobject]@{pid=$process.Id;start=$process.StartTime.ToUniversalTime().ToString('o');program=$exe;profileKind=$values.THE_ORDER_NATIVE_PROFILE_KIND;diagnostics=$false} | ConvertTo-Json
} finally {
    foreach ($name in $values.Keys) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
    foreach ($name in $prior.Keys) { [Environment]::SetEnvironmentVariable($name, $prior[$name], 'Process') }
}
if ($Wait -and $process) { $process.WaitForExit(); $code=$process.ExitCode; $process.Dispose(); exit $code }
if ($process) { $process.Dispose() }
