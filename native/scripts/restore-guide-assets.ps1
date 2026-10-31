param([string]$Project = 'native/TheOrder.WinUI/TheOrder.WinUI.csproj')
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sdk = 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/native-phase1/tools/dotnet/dotnet.exe'
$audit = Join-Path $repo ('.artifacts/walkthrough-overlay-20261029/lock-audit/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($audit) | Out-Null
$saved = @{}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repo 'native') -Filter packages.lock.json -Recurse -File) {
    if ($file.FullName -match '[\\/](bin|obj)[\\/]') { continue }
    $saved[$file.FullName] = [IO.File]::ReadAllBytes($file.FullName)
    [IO.File]::WriteAllBytes((Join-Path $audit ($file.Directory.Name + '.before.json')), $saved[$file.FullName])
}
Push-Location $repo
try {
    # Existing lock graphs have mixed RID contexts. Generate assets without changing source locks.
    & $sdk restore $Project -r win-x64 --verbosity minimal
    $restoreCode = $LASTEXITCODE
    foreach ($path in $saved.Keys) {
        [IO.File]::WriteAllBytes((Join-Path $audit ((Split-Path (Split-Path $path -Parent) -Leaf) + '.generated.json')), [IO.File]::ReadAllBytes($path))
    }
} finally {
    foreach ($path in $saved.Keys) { [IO.File]::WriteAllBytes($path, $saved[$path]) }
    Pop-Location
}
if ($restoreCode -ne 0) { throw "Asset restore failed: $restoreCode" }
@{ sourceLocksPreserved=$saved.Count; project=$Project; audit=$audit } | ConvertTo-Json
