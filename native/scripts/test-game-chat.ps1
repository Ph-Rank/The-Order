param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh output directory to preserve earlier evidence.'}
[IO.Directory]::CreateDirectory($output)|Out-Null
$env:TEMP=Join-Path $output 'temp';$env:TMP=$env:TEMP
[IO.Directory]::CreateDirectory($env:TEMP)|Out-Null
$vswhere='C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
$vs=(& $vswhere -latest -products '*' -property installationPath).Trim()
if(!$vs){throw 'Visual C++ build tools required'}
foreach($arch in @('x64','x86')){
    & (Join-Path $vs 'Common7/Tools/Launch-VsDevShell.ps1') -Arch $arch -HostArch amd64 -SkipAutomaticLocation|Out-Null
    $bin=Join-Path $output $arch;[IO.Directory]::CreateDirectory($bin)|Out-Null
    Push-Location $bin
    try{
        & cl.exe /nologo /std:c++17 /EHsc /O2 /MT /utf-8 /W4 /WX (Join-Path $repo 'native/TheOrder.GuideOverlay/tests/GuideChatTests.cpp') /link ('/OUT:'+(Join-Path $bin 'GuideChatTests.exe'))
        if($LASTEXITCODE){throw "Game chat test build failed: $arch"}
        & (Join-Path $bin 'GuideChatTests.exe') | Tee-Object -FilePath (Join-Path $bin 'result.json')
        if($LASTEXITCODE){throw "Game chat tests failed: $arch"}
    }finally{Pop-Location}
}
