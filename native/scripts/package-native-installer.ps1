param(
    [Parameter(Mandatory)][string]$CandidatePayload,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$InnoCompiler,
    [string]$Dotnet
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repo '.artifacts')).TrimEnd('\') + '\'
if (!$output.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)) {
    throw 'Use a fresh workspace artifact directory; completed installers are immutable.'
}
$originalHash = $null
[xml]$versionProperties = Get-Content -LiteralPath (Join-Path $repo 'native/Directory.Build.props') -Raw
$releaseVersion = [string]$versionProperties.Project.PropertyGroup.Version
$fileVersion = [string]$versionProperties.Project.PropertyGroup.FileVersion
$beta = $releaseVersion.Contains('-beta.')
$product = 'The Order'
$setupIdentity = 'TheOrder.Setup.F7940B13-6814-42F7-AB71-69A9901D7DB3'
$CandidatePayload = [IO.Path]::GetFullPath($CandidatePayload)
if (!$CandidatePayload.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Candidate must be a packaged workspace artifact.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $CandidatePayload 'TheOrder.WinUI.exe')).ProductVersion -ne $releaseVersion) { throw 'Build and package the exact source-controlled application version before creating this installer.' }
# A verified RC payload is not an installable release: the GUI entry always invokes launch.ps1.
# Reject it before copying files, invoking build tools, or creating the output directory.
function Assert-ReleaseEntryPoints([string]$Root) {
    $inputManifest = Get-Content -LiteralPath (Join-Path $Root 'files.json') -Raw | ConvertFrom-Json
    if ($inputManifest.schema -ne 1) { throw 'A release payload with a schema 1 file manifest is required.' }
    foreach ($name in @('launch.ps1', 'Start-The-Order.cmd', 'release-identity.json')) {
        $entries = @($inputManifest.files | Where-Object path -eq $name)
        $entryPath = Join-Path $Root $name
        if ($entries.Count -ne 1 -or !(Test-Path -LiteralPath $entryPath -PathType Leaf)) {
            throw "Installable release entry missing: $name. Use package-native-release.ps1; an RC package cannot be installed."
        }
        if ((Get-Item -LiteralPath $entryPath).Length -ne $entries[0].bytes -or
            (Get-FileHash -LiteralPath $entryPath).Hash -ne $entries[0].sha256) {
            throw "Installable release entry changed: $name. Repackage the release before building Setup."
        }
    }
    $identity = Get-Content -LiteralPath (Join-Path $Root 'release-identity.json') -Raw | ConvertFrom-Json
    if ($identity.version -cne $releaseVersion -or $identity.product -cne $product -or
        $identity.architecture -cne 'x64' -or $identity.selfContained -ne $true -or
        $identity.releaseState -cne 'STAGED_NOT_PUBLISHED') {
        throw 'The candidate must have the current x64 release identity produced by package-native-release.ps1.'
    }
    if ((Get-FileHash -LiteralPath (Join-Path $Root 'launch.ps1')).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'launch-native-release.ps1')).Hash) {
        throw 'The release launcher does not match the current maintained source. Repackage the release before building Setup.'
    }
}
Assert-ReleaseEntryPoints $CandidatePayload
$compiler = [IO.Path]::GetFullPath($InnoCompiler)
$compilerLibrary = Join-Path (Split-Path $compiler) 'ISCmplr.dll'
if (!(Test-Path -LiteralPath $compiler -PathType Leaf) -or !(Test-Path -LiteralPath $compilerLibrary -PathType Leaf) -or
    (Get-FileHash -LiteralPath $compiler).Hash -ne '0A8757031B33777E4C9CBFFEE40F11A5062B36D25CBE144C1DB73B6102B80AD7' -or
    (Get-FileHash -LiteralPath $compilerLibrary).Hash -ne '85A1E3090D3A5B85319F001B7C8F9ECFAD45F37EFF030A67BBE29EF58B7AA2C3') {
    throw 'The verified official Inno Setup 6.7.3 compiler is required.'
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'The installed Visual Studio C++ build tools are required for the GUI entry.' }
$vcVersion = (Get-Content -LiteralPath (Join-Path $vs 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt') -Raw).Trim()
$vc = Join-Path $vs ('VC/Tools/MSVC/' + $vcVersion)
$cl = Join-Path $vc 'bin/Hostx64/x64/cl.exe'
$sdk = (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots').KitsRoot10
$sdkVersion = '10.0.26100.0'
$rc = Join-Path $sdk "bin/$sdkVersion/x64/rc.exe"
if (!(Test-Path -LiteralPath $cl) -or !(Test-Path -LiteralPath $rc)) { throw 'Existing x64 compiler / Windows SDK 10.0.26100 is missing.' }
$payload = Join-Path $output 'payload'
$addon = Join-Path $output 'addon'
$build = Join-Path $output 'launcher-build'
$null = New-Item -ItemType Directory -Path $output, $addon, $build
Copy-Item -LiteralPath $CandidatePayload -Destination $payload -Recurse
$manifest = Get-Content -LiteralPath (Join-Path $payload 'files.json') -Raw | ConvertFrom-Json
$manifestHash = (Get-FileHash -LiteralPath (Join-Path $payload 'files.json')).Hash
$seen = @{}
if ($manifest.schema -ne 1 -or @($manifest.files).Count -lt 1 -or @(Get-ChildItem -LiteralPath $payload -Recurse -File).Count -ne @($manifest.files).Count + 1) { throw 'Incomplete payload manifest.' }
foreach ($file in $manifest.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $payload $file.path))
    if (!$path.StartsWith($payload + '\', [StringComparison]::OrdinalIgnoreCase) -or $seen.ContainsKey($path) -or
        !(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $file.bytes -or
        (Get-FileHash -LiteralPath $path).Hash -ne $file.sha256) { throw 'Published payload changed or escaped its directory.' }
    $seen[$path] = $true
}
Assert-ReleaseEntryPoints $payload
$installerSource = Join-Path $repo 'native/installer'
Copy-Item -LiteralPath (Join-Path $payload 'Assets/Brand.Light.ico') -Destination (Join-Path $build 'brand.ico')
$resourceText = [IO.File]::ReadAllText((Join-Path $installerSource 'Launcher.rc')).Replace('1,1,0,0', $fileVersion.Replace('.', ',')).Replace('1.1.0.0', $fileVersion).Replace('1.1.0', $releaseVersion)
[IO.File]::WriteAllText((Join-Path $build 'Launcher.rc'), $resourceText, [Text.UTF8Encoding]::new($false))
$oldPath = $env:PATH
Push-Location $build
try {
    $env:PATH = (Split-Path $cl) + ';' + (Split-Path $rc) + ';' + $oldPath
    $resourceArgs = @('/nologo', '/c65001', ('/fo' + (Join-Path $build 'Launcher.res')),
        ('/I' + (Join-Path $sdk "Include/$sdkVersion/um")),
        ('/I' + (Join-Path $sdk "Include/$sdkVersion/shared")), 'Launcher.rc')
    & $rc @resourceArgs 2>&1 | Tee-Object -FilePath (Join-Path $output 'resource-build.log') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Entry resource build failed.' }
    $compileArgs = @('/nologo', '/std:c++17', '/utf-8', '/O2', '/MT', '/EHsc', '/W4', '/WX', '/guard:cf',
        ('/I' + (Join-Path $vc 'include')),
        ('/I' + (Join-Path $sdk "Include/$sdkVersion/ucrt")),
        ('/I' + (Join-Path $sdk "Include/$sdkVersion/shared")),
        ('/I' + (Join-Path $sdk "Include/$sdkVersion/um")),
        ('/Fo' + (Join-Path $build 'Launcher.obj')), ('/Fe' + (Join-Path $addon 'TheOrder.exe')),
        (Join-Path $installerSource 'Launcher.cpp'), (Join-Path $build 'Launcher.res'), '/link',
        ('/LIBPATH:' + (Join-Path $vc 'lib/x64')),
        ('/LIBPATH:' + (Join-Path $sdk "Lib/$sdkVersion/ucrt/x64")),
        ('/LIBPATH:' + (Join-Path $sdk "Lib/$sdkVersion/um/x64")),
        '/SUBSYSTEM:WINDOWS', '/DYNAMICBASE', '/NXCOMPAT', '/HIGHENTROPYVA', '/GUARD:CF',
        '/MANIFEST:EMBED', "/MANIFESTUAC:level='asInvoker' uiAccess='false'", 'kernel32.lib', 'user32.lib', 'shell32.lib')
    & $cl @compileArgs 2>&1 | Tee-Object -FilePath (Join-Path $output 'launcher-build.log') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'GUI entry build failed.' }
} finally {
    Pop-Location
    $env:PATH = $oldPath
}
@"
[Installer]
Identity=$setupIdentity
Version=$releaseVersion
PublishedArchiveSha256=$originalHash
PublishedManifestSha256=$manifestHash
"@ | Set-Content -LiteralPath (Join-Path $addon 'installer-identity.ini') -Encoding ascii
Copy-Item -LiteralPath (Join-Path $repo ('docs/release/' + $releaseVersion + '/README.md')) -Destination (Join-Path $addon '安装版使用说明.txt')
$distribution = Join-Path $output 'distribution'
$null = New-Item -ItemType Directory -Path $distribution
# Inno evaluates Check before extraction. Unchanged files keep their bytes and timestamps.
# The exhaustive manifest, GUI entry, identity and notices remain owned by the usual uninstall log.
$entries = [Collections.Generic.List[string]]::new()
$checks = [Collections.Generic.List[string]]::new()
$checks.Add('function PayloadPathsSafe: Boolean;')
$checks.Add('begin')
$checks.Add('  Result := False;')
function Add-VerifiedEntry([string]$Source, [string]$Relative, [string]$Sha256) {
    if ($Relative -match '["\r\n]' -or $Relative.StartsWith('\') -or $Relative.Contains(':')) { throw 'Invalid installer-relative entry.' }
    $relativeWindows = $Relative.Replace('/', '\')
    $parent = [IO.Path]::GetDirectoryName($relativeWindows)
    $dest = '{app}'
    if ($parent) { $dest += '\' + $parent }
    $pascalPath = $relativeWindows.Replace("'", "''")
    $name = [IO.Path]::GetFileName($relativeWindows)
    $entries.Add('Source: "' + $Source + '"; DestDir: "' + $dest + '"; DestName: "' + $name +
        '"; Flags: ignoreversion; Check: PayloadFileNeedsUpdate(''' + $pascalPath + ''', ''' + $Sha256 + ''')')
    $checks.Add('  if not PayloadPathIsPlain(''' + $pascalPath + ''') then Exit;')
}
foreach ($file in @($manifest.files | Sort-Object path)) { Add-VerifiedEntry (Join-Path $payload $file.path) $file.path $file.sha256 }
Add-VerifiedEntry (Join-Path $payload 'files.json') 'files.json' $manifestHash
foreach ($name in @('TheOrder.exe','installer-identity.ini','安装版使用说明.txt')) {
    Add-VerifiedEntry (Join-Path $addon $name) $name (Get-FileHash -LiteralPath (Join-Path $addon $name)).Hash
}
Add-VerifiedEntry (Join-Path $installerSource 'LICENSE-InnoSetup.txt') 'licenses/LICENSE-InnoSetup.txt' (Get-FileHash -LiteralPath (Join-Path $installerSource 'LICENSE-InnoSetup.txt')).Hash
$checks.Add('  if not PayloadPathIsPlain(''update-base.bin'') then Exit;')
$checks.Add('  Result := True;')
$checks.Add('end;')
$payloadEntries = Join-Path $build 'verified-files.iss'
$payloadChecks = Join-Path $build 'verified-paths.iss'
[IO.File]::WriteAllLines($payloadEntries, $entries, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines($payloadChecks, $checks, [Text.UTF8Encoding]::new($false))
$compilerArgs = @('/Qp', ('/DPayloadDirectory=' + $payload), ('/DAddonDirectory=' + $addon),
    ('/DOutputDirectory=' + $distribution), ('/DPayloadEntries=' + $payloadEntries), ('/DPayloadChecks=' + $payloadChecks), (Join-Path $installerSource 'TheOrder.iss'))
$compilerArgs = @(('/DReleaseVersion=' + $releaseVersion), ('/DReleaseFileVersion=' + $fileVersion)) + $compilerArgs
if ($beta) { $compilerArgs = @('/DPublicBeta=1') + $compilerArgs }
& $compiler @compilerArgs 2>&1 | Tee-Object -FilePath (Join-Path $output 'installer-build.log') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Setup compiler failed.' }
$setup = Join-Path $distribution ('The-Order-' + $releaseVersion + '-Setup-x64.exe')
@{schema=1;profileContract=1;version=$releaseVersion;installationIdentity=$setupIdentity} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $distribution 'update-channel.json') -Encoding utf8

# Publish the block map alongside Setup. Checks read only Release metadata; maps are fetched on explicit Install.
if (!$Dotnet) { $Dotnet = Join-Path $repo '.artifacts/native-phase1/tools/dotnet/dotnet.exe' }
if (!(Test-Path -LiteralPath $Dotnet -PathType Leaf) -or (& $Dotnet --version) -ne '10.0.401') { throw 'The pinned .NET SDK 10.0.401 is required for the block map.' }
$updateTool = Join-Path $repo 'native/TheOrder.UpdateTool/TheOrder.UpdateTool.csproj'
& $Dotnet restore $updateTool --locked-mode 2>&1 | Tee-Object -FilePath (Join-Path $output 'blockmap-restore.log') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Block-map tool locked restore failed.' }
$map = $setup + '.blocks.json'
if (!$beta) { & $Dotnet run --project $updateTool --configuration Release --no-restore -- blockmap $setup $releaseVersion $map 2>&1 |
    Tee-Object -FilePath (Join-Path $output 'blockmap-build.log') | Out-Null
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $map)) { throw 'Block-map generation failed.' }
}
$sourceRoot = Join-Path $output 'installer-source'
$null = New-Item -ItemType Directory -Path (Join-Path $sourceRoot 'native/scripts')
Copy-Item -LiteralPath $installerSource -Destination (Join-Path $sourceRoot 'native') -Recurse
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $sourceRoot 'native/scripts')
Copy-Item -LiteralPath (Join-Path $repo 'native/scripts/test-native-installer.ps1') -Destination (Join-Path $sourceRoot 'native/scripts')
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $sourceRoot
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{path=[IO.Path]::GetRelativePath($sourceRoot, $_.FullName);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
})
@{schema=1;version=$releaseVersion;purpose='Installer and GUI-entry source supplement; application changes require the corresponding complete source';
    correspondingApplicationSource=('The-Order-' + $releaseVersion + '-source.zip');originalArchiveSha256=$originalHash;
    innoSetup='6.7.3';windowsSdk=$sdkVersion;visualCpp=$vcVersion;files=$sourceFiles} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $sourceRoot 'INSTALLER-SOURCE-MANIFEST.json') -Encoding utf8
Compress-Archive -LiteralPath (Join-Path $sourceRoot 'native'), (Join-Path $sourceRoot 'LICENSE'),
    (Join-Path $sourceRoot 'INSTALLER-SOURCE-MANIFEST.json') -DestinationPath (Join-Path $distribution ('The-Order-' + $releaseVersion + '-installer-source.zip'))
$hashLines = @(Get-ChildItem -LiteralPath $distribution -File | Sort-Object Name | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() + '  ' + $_.Name
})
[IO.File]::WriteAllLines((Join-Path $distribution 'INSTALLER-SHA256SUMS.txt'), $hashLines, [Text.UTF8Encoding]::new($false))
$receipt = [ordered]@{schema=1;version=$releaseVersion;createdUtc=[DateTimeOffset]::UtcNow.ToString('o');
    installer=$setup;installerBytes=(Get-Item -LiteralPath $setup).Length;installerSha256=(Get-FileHash -LiteralPath $setup).Hash;
    blockMap=$(if(!$beta){$map}else{$null});blockMapBytes=$(if(!$beta){(Get-Item -LiteralPath $map).Length}else{0});blockMapSha256=$(if(!$beta){(Get-FileHash -LiteralPath $map).Hash}else{$null});
    differentialAlgorithm='gear-v1-16k-64k-128k-sha256';compression='separate lzma2 streams';baseCache='update-base.bin (one installer, owned by uninstall)';
    selectiveInstall='SHA-256 comparison skips unchanged payload files';selectiveEntries=$entries.Count;
    originalArchiveSha256=$originalHash;originalManifestSha256=$manifestHash;payloadFiles=@($manifest.files).Count + 1;
    originalApplicationUnchanged=(!$CandidatePayload);signing='unsigned';profileOpened=$false;status='BUILT_NOT_YET_VALIDATED'}
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'build-receipt.json') -Encoding utf8
$receipt | ConvertTo-Json -Depth 5
