param(
    [Parameter(Mandatory)][string]$PublishedTool,
    [Parameter(Mandatory)][string]$PublishedApplication
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd('\')
$artifactRoot=Join-Path $repo '.artifacts'
function Plain([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (!$full.StartsWith($artifactRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Workspace artifact directory required' }
    for($part=[IO.DirectoryInfo]::new($full);$part;$part=$part.Parent) {
        if ((Test-Path -LiteralPath $part.FullName) -and ((Get-Item -LiteralPath $part.FullName -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked output refused' }
    }
    return $full
}
$tool=Plain $PublishedTool; $application=Plain $PublishedApplication
if ($tool.Equals($application,[StringComparison]::OrdinalIgnoreCase) -or $tool.StartsWith($application+'\',[StringComparison]::OrdinalIgnoreCase)
    -or $application.StartsWith($tool+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Independent publish directories required' }
foreach($entry in @('TheOrder.MigrationTool.exe','TheOrder.MigrationTool.dll','TheOrder.MigrationTool.deps.json','TheOrder.MigrationTool.runtimeconfig.json','hostfxr.dll','hostpolicy.dll','coreclr.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $tool $entry) -PathType Leaf)) { throw 'Incomplete self-contained migration tool' }
}
if (!(Test-Path -LiteralPath (Join-Path $application 'TheOrder.WinUI.exe') -PathType Leaf)) { throw 'Published application required' }
if (Test-Path -LiteralPath (Join-Path $application 'files.json')) { throw 'Attach tool before freezing the release manifest' }
$runtime=Get-Content -LiteralPath (Join-Path $tool 'TheOrder.MigrationTool.runtimeconfig.json') -Raw|ConvertFrom-Json
if (!@($runtime.runtimeOptions.includedFrameworks | Where-Object name -EQ 'Microsoft.NETCore.App').Count) { throw 'Tool must carry its own .NET runtime' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $tool 'TheOrder.MigrationTool.exe')).ProductVersion -cne
    [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $application 'TheOrder.WinUI.exe')).ProductVersion) { throw 'Application and migration version differ' }
$destination=Plain (Join-Path $application 'Tools/LibraryMigration')
if (Test-Path -LiteralPath $destination) { throw 'Fresh tool destination required' }
$files=@(Get-ChildItem -LiteralPath $tool -Recurse -File -Force)
foreach($file in $files) {
    if ($file.Name -match '(?i)(^\.env|^\.dev\.vars|secret|credential)' -or $file.Extension -in @('.cs','.ts','.map','.pdb')) { throw 'Development or sensitive artifact refused' }
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked payload refused' }
}
New-Item -ItemType Directory -Path $destination -Force|Out-Null
foreach($file in $files) {
    $relative=[IO.Path]::GetRelativePath($tool,$file.FullName)
    $target=Join-Path $destination $relative
    if (![IO.Path]::GetFullPath($target).StartsWith($destination+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Escaped payload' }
    New-Item -ItemType Directory -Path (Split-Path $target) -Force|Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
    if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw 'Tool payload copy mismatch' }
}
Copy-Item -LiteralPath (Join-Path $repo 'docs/migration-trial.md') -Destination (Join-Path $destination '导入操作步骤.md')
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $destination 'LICENSE-TheOrder.txt')
Copy-Item -LiteralPath (Join-Path $repo 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $destination 'THIRD_PARTY_NOTICES.md')
[pscustomobject]@{status='ATTACHED_BEFORE_MANIFEST';toolFiles=$files.Count;selfContained=$true;source=([IO.Path]::GetRelativePath($repo,$tool));destination='Tools/LibraryMigration';productionImport=$false}|ConvertTo-Json
