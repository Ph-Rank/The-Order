param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidateSet('GuideChatTests','GuidePresentationTests')][string]$Fixture
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh output directory to preserve earlier evidence.' }
[IO.Directory]::CreateDirectory($output) | Out-Null
$env:TEMP = Join-Path $output 'temp'
$env:TMP = $env:TEMP
[IO.Directory]::CreateDirectory($env:TEMP) | Out-Null
$vswhere = 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
$vs = (& $vswhere -latest -products '*' -property installationPath).Trim()
if (!$vs) { throw 'Visual C++ build tools required' }

$source = Join-Path $repo "native/TheOrder.GuideOverlay/tests/$Fixture.cpp"
foreach ($arch in 'x64','x86') {
    & (Join-Path $vs 'Common7/Tools/Launch-VsDevShell.ps1') -Arch $arch -HostArch amd64 -SkipAutomaticLocation | Out-Null
    $bin = [IO.Directory]::CreateDirectory((Join-Path $output $arch)).FullName
    $executable = Join-Path $bin "$Fixture.exe"
    Push-Location $bin
    try {
        & cl.exe /nologo /std:c++17 /EHsc /O2 /MT /utf-8 /W4 /WX $source /link "/OUT:$executable"
        if ($LASTEXITCODE) { throw "$Fixture build failed for $arch." }
        & $executable | Tee-Object -FilePath (Join-Path $bin 'result.json')
        if ($LASTEXITCODE) { throw "$Fixture failed for $arch." }
    }
    finally { Pop-Location }
}
