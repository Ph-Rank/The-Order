param([Parameter(Mandatory)][string]$PublishedDirectory, [Parameter(Mandatory)][string]$OutputDirectory, [string]$SourceManifest, [string]$Packages = (Join-Path $env:USERPROFILE '.nuget/packages'), [string]$DocumentationDirectory, [switch]$IncludeTestLauncher)
$ErrorActionPreference='Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = [IO.Path]::GetFullPath($PublishedDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repo '.artifacts')).TrimEnd('\') + '\'
if (!$output.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)) { throw 'Fresh workspace artifact output required; existing candidates are immutable.' }
[xml]$versionProperties = Get-Content -LiteralPath (Join-Path $repo 'native/Directory.Build.props') -Raw
$releaseVersion = [string]$versionProperties.Project.PropertyGroup.Version
$fileVersion = [string]$versionProperties.Project.PropertyGroup.FileVersion
$beta = $releaseVersion.Contains('-beta.')
$product = 'The Order'
$documentation = if ($DocumentationDirectory) { [IO.Path]::GetFullPath($DocumentationDirectory) } else { Join-Path $repo ('docs/release/' + $releaseVersion) }
if (!$documentation.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Release documentation must come from this source checkout.' }
foreach ($name in @('README.md','CHANGELOG.md','BUILD.md','AUTHENTICATION.md')) {
    if (!(Test-Path -LiteralPath (Join-Path $documentation $name) -PathType Leaf)) { throw 'Complete, source-controlled release documentation is required.' }
}
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $source 'TheOrder.WinUI.exe'))
if ($version.FileVersion -ne $fileVersion -or $version.ProductVersion -ne $releaseVersion) { throw 'Publish the exact source-controlled application version before packaging.' }
$sourceIdentity = $null
if ($SourceManifest) {
    $sourceManifestPath = [IO.Path]::GetFullPath($SourceManifest)
    $frozen = Get-Content -LiteralPath $sourceManifestPath -Raw | ConvertFrom-Json
    foreach ($file in $frozen.items) {
        $path = [IO.Path]::GetFullPath((Join-Path $repo $file.path))
        if (!$path.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path).Hash -ne $file.sha256) { throw 'Source changed after freeze; rebuild and use a fresh source checkpoint.' }
    }
    $sourceIdentity = @{manifestSha256=(Get-FileHash -LiteralPath $sourceManifestPath).Hash;files=@($frozen.items).Count;head=$frozen.head;branch=$frozen.branch;dirty=$(if ($frozen.PSObject.Properties.Name -contains 'dirty') { [bool]$frozen.dirty } else { $true })}
}
# Reuse the checked runtime/CRT/license assembly; the isolated RC launcher remains available.
& (Join-Path $PSScriptRoot 'package-native-rc.ps1') -PublishedDirectory $source -OutputDirectory $output -Packages $Packages | Out-Null
$licenseRoot = Join-Path $output 'licenses'
$licenseIndexPath = Join-Path $licenseRoot 'dependency-license-index.json'
$licenseIndex = Get-Content -LiteralPath $licenseIndexPath -Raw | ConvertFrom-Json
$runtimeConfig = Get-Content -LiteralPath (Join-Path $source 'TheOrder.WinUI.runtimeconfig.json') -Raw | ConvertFrom-Json
$runtimeVersion = ($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
$mitName = 'Microsoft.NETCore.App.Runtime.win-x64-' + $runtimeVersion + '-LICENSE.TXT'
if (!(Test-Path -LiteralPath (Join-Path $licenseRoot $mitName))) { throw 'The unmodified .NET Foundation MIT license is required.' }
$assets = Get-Content -LiteralPath (Join-Path $repo 'native/TheOrder.WinUI/obj/project.assets.json') -Raw | ConvertFrom-Json
$dependencies = @(foreach ($dependency in $licenseIndex) {
    $specPath = Join-Path $licenseRoot ($dependency.package.Replace('/','-') + '.nuspec')
    if (!(Test-Path -LiteralPath $specPath)) { throw 'Dependency license metadata is missing.' }
    [xml]$spec = Get-Content -LiteralPath $specPath -Raw
    $license = $spec.package.metadata.license
    $texts = @($dependency.licenseFiles | Where-Object { $_ } | ForEach-Object { 'licenses/' + $dependency.package.Replace('/','-') + '-' + $_ })
    # MIT expression-only packages share the unmodified .NET Foundation license;
    # Markdig's own BSD notice is copied by the application's publish target.
    if ($license -and $license.type -eq 'expression' -and $license.InnerText -eq 'MIT') { $texts += 'licenses/' + $mitName }
    if ($dependency.package -like 'Markdig/*') { $texts += 'LICENSE-Markdig.txt' }
    if ($dependency.package -eq 'LiteDB/4.1.4') { $texts += 'licenses/LITEDB-LICENSE.txt' }
    if ($dependency.package -like 'LevelDB.Standard/*') { $texts += 'licenses/LEVELDB-LICENSE.txt', 'licenses/SNAPPY-LICENSE.txt' }
    foreach ($text in $texts) { if (!(Test-Path -LiteralPath (Join-Path $output $text))) { throw 'Dependency license text is missing.' } }
    $lock = $assets.libraries.PSObject.Properties[$dependency.package]
    $hashPath = Join-Path (Join-Path $Packages $dependency.package.ToLowerInvariant()) ($spec.package.metadata.id.ToLowerInvariant() + '.' + $spec.package.metadata.version + '.nupkg.sha512')
    $contentHash = if ($lock) { $lock.Value.sha512 } elseif (Test-Path -LiteralPath $hashPath) { (Get-Content -LiteralPath $hashPath -Raw).Trim() } else { $null }
    [ordered]@{package=$dependency.package;contentHash=$contentHash;licenseType=if($license){$license.type}else{'upstream-url'};license=if($license){$license.InnerText}else{$null};licenseUrl=$spec.package.metadata.licenseUrl;licenseTexts=@($texts | Sort-Object -Unique);metadata='licenses/' + $dependency.package.Replace('/','-') + '.nuspec';repository=$spec.package.metadata.repository.url;developmentMetadataOnly=($dependency.package -like 'Microsoft.Windows.SDK.BuildTools*')}
})
@{schema=1;source='Locked project.assets.json and unmodified NuGet nuspec/license files';runtime=$runtimeVersion;packages=$dependencies;developmentMetadata='Build-tools metadata is retained for provenance; application packages do not ship build tools.'} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'dependencies.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'launch-native-release.ps1') -Destination (Join-Path $output 'launch.ps1')
$entry = '@echo off' + "`r`n" + 'setlocal' + "`r`n" + 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0launch.ps1"' + "`r`n" + 'set "theOrderExit=%errorlevel%"' + "`r`n" + 'if not "%theOrderExit%"=="0" pause' + "`r`n" + 'exit /b %theOrderExit%' + "`r`n"
[IO.File]::WriteAllText((Join-Path $output 'Start-The-Order.cmd'), $entry, [Text.Encoding]::ASCII)
$testEntry = $entry.Replace('launch.ps1"', 'launch.ps1" -Test')
if ($IncludeTestLauncher) { [IO.File]::WriteAllText((Join-Path $output 'Start-Test.cmd'), $testEntry, [Text.Encoding]::ASCII) }
else { Remove-Item -LiteralPath (Join-Path $output 'Start-Test.cmd') }
foreach ($name in @('README.md','CHANGELOG.md','BUILD.md','AUTHENTICATION.md','ROLLBACK.md','HUMAN-CHECKS.md')) {
    $document = Join-Path $documentation $name
    if (Test-Path -LiteralPath $document -PathType Leaf) { Copy-Item -LiteralPath $document -Destination (Join-Path $output $name) }
}
# These are individual files created above, confined to this fresh output.
Remove-Item -LiteralPath (Join-Path $output 'README-RC.txt')
Remove-Item -LiteralPath (Join-Path $output 'launch-test.ps1')
$identityPath = Join-Path $output 'release-identity.json'
$identity = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$identity | Add-Member -NotePropertyName version -NotePropertyValue $releaseVersion
$identity | Add-Member -NotePropertyName product -NotePropertyValue $product
$identity | Add-Member -NotePropertyName channel -NotePropertyValue $(if($beta){'beta'}else{'stable'})
$identity | Add-Member -NotePropertyName releaseState -NotePropertyValue 'STAGED_NOT_PUBLISHED'
$identity | Add-Member -NotePropertyName codeSigning -NotePropertyValue 'unsigned'
$identity | Add-Member -NotePropertyName sourceSnapshot -NotePropertyValue $sourceIdentity
$identity | Add-Member -NotePropertyName correspondingSource -NotePropertyValue ('The-Order-' + $releaseVersion + '-source.zip')
$identity.gate = 'See the separately bound release acceptance report; packaging does not certify acceptance.'
$identity.cleanWindows = 'Current candidate clean Windows NOT VERIFIED; see bound validation report. Prior Win11 evidence is not Win10 evidence.'
$identity.crtSource = 'Installed Visual Studio licensed x64 REDIST directory'
$identity | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $identityPath -Encoding utf8
$files=@(Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object Name -ne 'files.json' | Sort-Object FullName | ForEach-Object { @{path=[IO.Path]::GetRelativePath($output,$_.FullName);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
@{schema=1;files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'files.json') -Encoding utf8
[pscustomobject]@{package=$output;version=$releaseVersion;files=$files.Count;manifestSha256=(Get-FileHash -LiteralPath (Join-Path $output 'files.json')).Hash;releaseState='STAGED_NOT_PUBLISHED'} | ConvertTo-Json
