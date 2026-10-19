param([Parameter(Mandatory)][string]$Package, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=[IO.Path]::GetFullPath($OutputDirectory)
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $repo '.artifacts')).TrimEnd('\')+'\'
if (!$output.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)) { throw 'Fresh task artifact output required.' }
$null=[IO.Directory]::CreateDirectory($output)
$shell=Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$results=[Collections.Generic.List[object]]::new()
function Invoke-Validation([string]$Script,[string[]]$Arguments) {
    $start=[Diagnostics.ProcessStartInfo]::new($shell)
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    foreach($argument in @('-NoProfile','-ExecutionPolicy','Bypass','-File',$Script)+$Arguments) { $start.ArgumentList.Add($argument) }
    $process=[Diagnostics.Process]::Start($start)
    try {
        $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(30000)) { throw 'Validation child timed out; it was retained.' }
        [pscustomobject]@{exitCode=$process.ExitCode;stdout=$stdout.GetAwaiter().GetResult();stderr=$stderr.GetAwaiter().GetResult()}
    } finally { $process.Dispose() }
}
function Record([string]$Name,$Result,[bool]$ShouldPass) {
    $passed=if($ShouldPass){$Result.exitCode -eq 0}else{$Result.exitCode -ne 0}
    $results.Add([pscustomobject]@{name=$Name;passed=$passed;expectedSuccess=$ShouldPass;exitCode=$Result.exitCode;stdout=$Result.stdout;stderr=$Result.stderr})
    $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
    if (!$passed) { throw "Launcher regression: $Name" }
}
Record 'real-package-validation-without-opening-Production' (Invoke-Validation (Join-Path ([IO.Path]::GetFullPath($Package)) 'launch.ps1') @('-ValidateOnly')) $true
function Fixture([string]$Name) {
    $folder=Join-Path $output $Name
    $null=[IO.Directory]::CreateDirectory($folder)
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'launch-native-release.ps1') -Destination (Join-Path $folder 'launch.ps1')
    Copy-Item -LiteralPath (Join-Path ([IO.Path]::GetFullPath($Package)) 'release-identity.json') -Destination (Join-Path $folder 'release-identity.json')
    [IO.File]::WriteAllBytes((Join-Path $folder 'TheOrder.WinUI.exe'),[byte[]](1,2,3,4))
    $item=[pscustomobject]@{path='TheOrder.WinUI.exe';bytes=4;sha256=(Get-FileHash -LiteralPath (Join-Path $folder 'TheOrder.WinUI.exe')).Hash}
    @{schema=1;files=@($item)}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $folder 'files.json') -Encoding utf8
    return $folder
}
function RunFixture([string]$Name,[scriptblock]$Mutate,[bool]$ShouldPass,[string[]]$Arguments=@('-ValidateOnly')) {
    $folder=Fixture $Name
    & $Mutate $folder
    Record $Name (Invoke-Validation (Join-Path $folder 'launch.ps1') $Arguments) $ShouldPass
}
RunFixture 'complete-fixture-no-process-start' {} $true
foreach ($withBom in @($false,$true)) {
    RunFixture ('unicode-manifest-bom-'+$withBom) {param($f)
        $name='阅读说明🌸.md'; $path=Join-Path $f $name
        [IO.File]::WriteAllText($path,'Unicode manifest fixture',[Text.UTF8Encoding]::new($false))
        $manifest=Get-Content -LiteralPath (Join-Path $f 'files.json') -Raw -Encoding UTF8|ConvertFrom-Json
        $manifest.files += [pscustomobject]@{path=$name;bytes=(Get-Item -LiteralPath $path).Length;sha256=(Get-FileHash -LiteralPath $path).Hash}
        [IO.File]::WriteAllText((Join-Path $f 'files.json'),($manifest|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($withBom))
    } $true
}
RunFixture 'missing-executable' {param($f) Remove-Item -LiteralPath (Join-Path $f 'TheOrder.WinUI.exe')} $false
RunFixture 'changed-executable' {param($f) [IO.File]::WriteAllBytes((Join-Path $f 'TheOrder.WinUI.exe'),[byte[]](4,3,2,1))} $false
RunFixture 'wrong-size' {param($f) $m=Get-Content -LiteralPath (Join-Path $f 'files.json') -Raw|ConvertFrom-Json; $m.files[0].bytes=3; $m|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $f 'files.json')} $false
RunFixture 'empty-manifest' {param($f) @{schema=1;files=@()}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $f 'files.json')} $false
RunFixture 'invalid-json' {param($f) [IO.File]::WriteAllText((Join-Path $f 'files.json'),'{')} $false
RunFixture 'future-schema' {param($f) $m=Get-Content -LiteralPath (Join-Path $f 'files.json') -Raw|ConvertFrom-Json; $m.schema=2; $m|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $f 'files.json')} $false
foreach($badPath in @('../outside.txt','C:\outside.txt','TheOrder.WinUI.exe:stream')) {
    $index=[array]::IndexOf(@('../outside.txt','C:\outside.txt','TheOrder.WinUI.exe:stream'),$badPath)
    RunFixture "escaped-path-$index" {param($f) $m=Get-Content -LiteralPath (Join-Path $f 'files.json') -Raw|ConvertFrom-Json; $m.files += [pscustomobject]@{path=$badPath;bytes=0;sha256='00'}; $m|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $f 'files.json')} $false
}
RunFixture 'case-insensitive-duplicate' {param($f) $m=Get-Content -LiteralPath (Join-Path $f 'files.json') -Raw|ConvertFrom-Json; $m.files += [pscustomobject]@{path='THEORDER.WINUI.EXE';bytes=4;sha256=$m.files[0].sha256}; $m|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $f 'files.json')} $false
$testRoot=Join-Path $output 'synthetic-profile'
$null=[IO.Directory]::CreateDirectory($testRoot)
@{ProfileKind='Test'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $testRoot 'native-profile.json')
RunFixture 'explicit-isolated-Test' {} $true @('-Test','-DataDirectory',$testRoot,'-ValidateOnly')
RunFixture 'relative-Test-path' {} $false @('-Test','-DataDirectory','relative-profile','-ValidateOnly')
RunFixture 'custom-Production-path-forbidden' {} $false @('-DataDirectory',$testRoot,'-ValidateOnly')
foreach($formal in @((Join-Path $env:APPDATA 'KunGalManager'),(Join-Path $env:LOCALAPPDATA 'TheOrder/Profiles/Production'),(Join-Path $env:LOCALAPPDATA 'TheOrder/Profiles/Development'))) {
    $index=@($results).Count
    RunFixture "formal-root-$index" {} $false @('-Test','-DataDirectory',$formal,'-ValidateOnly')
    RunFixture "formal-child-$index" {} $false @('-Test','-DataDirectory',(Join-Path $formal 'child'),'-ValidateOnly')
    RunFixture "formal-parent-$index" {} $false @('-Test','-DataDirectory',([IO.Path]::GetDirectoryName($formal)),'-ValidateOnly')
}
@{ProfileKind='Production'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $testRoot 'native-profile.json')
RunFixture 'wrong-existing-profile-kind' {} $false @('-Test','-DataDirectory',$testRoot,'-ValidateOnly')
[IO.File]::WriteAllText((Join-Path $testRoot 'native-profile.json'),'{')
RunFixture 'corrupt-profile-manifest' {} $false @('-Test','-DataDirectory',$testRoot,'-ValidateOnly')
$linkTarget=Join-Path $output 'synthetic-link-target'; $link=Join-Path $output 'synthetic-link'
$null=[IO.Directory]::CreateDirectory($linkTarget)
$null=New-Item -ItemType Junction -Path $link -Target $linkTarget
RunFixture 'linked-profile-forbidden' {} $false @('-Test','-DataDirectory',$link,'-ValidateOnly')
[pscustomobject]@{passed=$results.Count;failed=0;shell='Windows PowerShell 5.1';ordinaryProductionLaunched=$false;realGameLaunched=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $output 'summary.json')
