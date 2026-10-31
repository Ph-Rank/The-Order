param([ValidateSet('x86','x64','all')][string]$Architecture='all', [string]$WorkDirectory, [switch]$SkipEngine)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $WorkDirectory) {$WorkDirectory=Join-Path $repo '.artifacts/guide-overlay-build'}
$work=[IO.Path]::GetFullPath($WorkDirectory)
$source=Join-Path $repo 'native/TheOrder.GuideOverlay'
$payload=Join-Path $source 'payload'
$engine=Join-Path $work 'engine-source';$detours=Join-Path $work 'detours-source'
[IO.Directory]::CreateDirectory($work) | Out-Null
function Invoke-Checked([scriptblock]$Action) { & $Action; if ($LASTEXITCODE -ne 0) {throw "Native build failed: $LASTEXITCODE"} }
if (-not (Test-Path -LiteralPath (Join-Path $engine '.git'))) {
 Invoke-Checked {git clone --branch v6.8.0 --depth 1 --recurse-submodules --shallow-submodules https://github.com/crosire/reshade.git $engine}
}
if ((git -C $engine rev-parse HEAD).Trim() -ne '18deaa52de0c425a78b329e9cb3c497281cd00ec') {throw 'Unpinned overlay engine'}
if (-not (Test-Path -LiteralPath (Join-Path $detours '.git'))) {
 Invoke-Checked {git clone --branch v4.0.1 --depth 1 https://github.com/microsoft/Detours.git $detours}
}
if ((git -C $detours rev-parse HEAD).Trim() -ne 'e4bfd6b03e50de46b47abfbd1e46b384f0c5f833') {throw 'Unpinned launch adapter'}
$vswhere='C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
$vs=(& $vswhere -latest -products '*' -property installationPath).Trim()
if (-not $vs) {throw 'Visual C++ build tools required'}
$msbuild=Join-Path $vs 'MSBuild/Current/Bin/amd64/MSBuild.exe'
$engineEntry=Join-Path $engine 'source/dll_main.cpp'
$text=[IO.File]::ReadAllText($engineEntry).Replace("`r`n","`n")
if (-not $text.Contains('THE_ORDER_RESTORE_IMPORTS')) {
 $text=$text.Replace('#include <delayimp.h>', "// THE_ORDER_RESTORE_IMPORTS: restore the owned launch import table once loaded.`nextern `"C`" BOOL WINAPI DetourRestoreAfterWith(void);`n#include <delayimp.h>")
 $text=$text.Replace("case DLL_PROCESS_ATTACH:`n`t`t{", "case DLL_PROCESS_ATTACH:`n`t`t{`n`t`t`tDetourRestoreAfterWith();")
 if (-not $text.Contains('DetourRestoreAfterWith();')) {throw 'Pinned engine entry shape changed'}
 [IO.File]::WriteAllText($engineEntry,$text,[Text.UTF8Encoding]::new($false))
}
$guiPath=Join-Path $engine 'source/runtime_gui.cpp'
$gui=[IO.File]::ReadAllText($guiPath)
$gui=$gui.Replace('const bool show_splash_window = _show_splash &&', 'const bool show_splash_window = false &&')
if(-not $gui.Contains('THE_ORDER_READING_FONTS')) {
 $fontAnchor="`tImGui::SetCurrentContext(backup_context);`r`n`r`n`t_rebuild_font_atlas = false;"
 if(-not $gui.Contains($fontAnchor)){$fontAnchor=$fontAnchor.Replace("`r`n","`n")}
 if(-not $gui.Contains($fontAnchor)){throw 'Pinned font atlas shape changed'}
 $fontPatch=@'
    // THE_ORDER_READING_FONTS: own session only, use installed fonts without redistribution.
    if (std::getenv("THE_ORDER_GUIDE_SESSION") != nullptr)
    {
        std::filesystem::path title_font = L"C:/Windows/Fonts/simsun.ttc";
        if (std::filesystem::exists(title_font, ec))
            add_font_from_file(title_font, nullptr, ec);
    }

'@
 $gui=$gui.Replace($fontAnchor,$fontPatch+$fontAnchor)
}
$gui=$gui.Replace('GetEnvironmentVariableW(L"THE_ORDER_GUIDE_SESSION", nullptr, 0) != 0','std::getenv("THE_ORDER_GUIDE_SESSION") != nullptr')
if(-not $gui.Contains('#include <cstdlib>')){$gui=$gui.Replace('#include "runtime.hpp"',"#include <cstdlib>`n#include `"runtime.hpp`"")}
[IO.File]::WriteAllText($guiPath,$gui,[Text.UTF8Encoding]::new($false))
if($SkipEngine){
 $previousManifest=Join-Path $payload 'payload.json'
 if(-not(Test-Path -LiteralPath $previousManifest)){throw 'A complete font-aware runtime build is required first'}
 $previous=Get-Content -LiteralPath $previousManifest -Raw|ConvertFrom-Json
 if($previous.readingFontRoles -ne 1 -or $previous.engineGuiSha256 -ne (Get-FileHash -LiteralPath $guiPath).Hash){throw 'Runtime source changed; rebuild without SkipEngine'}
}
$projectPath=Join-Path $engine 'ReShade.vcxproj'
$project=[IO.File]::ReadAllText($projectPath)
if (-not $project.Contains('TheOrder.props')) {
 $project=$project.Replace('<Import Project="Common.props" />','<Import Project="Common.props" /><Import Project="TheOrder.props" />')
 [IO.File]::WriteAllText($projectPath,$project,[Text.UTF8Encoding]::new($false))
}
$props=@"
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003"><ItemDefinitionGroup><Link><AdditionalDependencies>$([Security.SecurityElement]::Escape($work))/native-bin/`$(Platform)/detours.lib;%(AdditionalDependencies)</AdditionalDependencies></Link></ItemDefinitionGroup></Project>
"@
$propsPath=Join-Path $engine 'TheOrder.props'
if(-not(Test-Path -LiteralPath $propsPath) -or [IO.File]::ReadAllText($propsPath) -ne $props){[IO.File]::WriteAllText($propsPath,$props,[Text.UTF8Encoding]::new($false))}
foreach ($arch in $(if($Architecture -eq 'all') {@('x64','x86')} else {@($Architecture)})) {
 & (Join-Path $vs 'Common7/Tools/Launch-VsDevShell.ps1') -Arch $arch -HostArch amd64 -SkipAutomaticLocation | Out-Null
 $platform=if($arch -eq 'x86') {'Win32'} else {'x64'}
 $machine=if($arch -eq 'x86') {'X86'} else {'X64'}
 $intermediate=Join-Path $work ('native-bin/'+$platform);$out=Join-Path $payload $arch
 [IO.Directory]::CreateDirectory($intermediate) | Out-Null;[IO.Directory]::CreateDirectory($out) | Out-Null
 Push-Location (Join-Path $detours 'src')
 try {Invoke-Checked {nmake.exe /nologo "DETOURS_TARGET_PROCESSOR=$machine" 'CFLAGS=/W4 /WX /MT /Gy /O2 /Zl /DWIN32_LEAN_AND_MEAN /D_WIN32_WINNT=0xA00'}} finally {Pop-Location}
 Copy-Item -LiteralPath (Join-Path $detours ('lib.'+$machine+'/detours.lib')) -Destination (Join-Path $intermediate 'detours.lib') -Force
 $sdk=Join-Path $engine 'include';$imgui=Join-Path $engine 'deps/imgui'
 $common=@('/nologo','/std:c++17','/EHsc','/O2','/MT','/utf-8','/W4','/WX',('/I'+$sdk),('/I'+$imgui),('/I'+(Join-Path $detours 'include')))
 Push-Location $intermediate
 try {
  Invoke-Checked {cl.exe @common (Join-Path $source 'GuideAddon.cpp') /link /DLL ('/IMPLIB:'+(Join-Path $intermediate 'GuideAddon.lib')) ('/OUT:'+(Join-Path $payload ('TheOrder.Guide.addon'+$(if($arch -eq 'x86'){'32'}else{'64'})))) user32.lib}
  Invoke-Checked {cl.exe @common (Join-Path $source 'GuideHost.cpp') /link /SUBSYSTEM:WINDOWS ('/OUT:'+(Join-Path $out 'TheOrder.GuideHost.exe')) (Join-Path $intermediate 'detours.lib') shell32.lib}
  Invoke-Checked {cl.exe @common (Join-Path $source 'GuideGameFixture.cpp') /link /SUBSYSTEM:WINDOWS ('/OUT:'+(Join-Path $intermediate 'GuideGameFixture.exe'))}
 } finally {Pop-Location}
 if (-not $SkipEngine) {
  Push-Location $engine
  try {
   Invoke-Checked {& $msbuild ReShade.vcxproj /p:Configuration=Release "/p:Platform=$platform" "/p:SolutionDir=$engine\" /m:2 /v:minimal /nologo}
  } finally {Pop-Location}
  Copy-Item -LiteralPath (Join-Path $engine ('bin/'+$platform+'/Release/ReShade'+$(if($arch -eq 'x86'){'32'}else{'64'})+'.dll')) -Destination (Join-Path $out 'TheOrder.GuideRuntime.dll') -Force
 }
}
$licenses=Join-Path $payload 'licenses';[IO.Directory]::CreateDirectory($licenses)|Out-Null
Copy-Item -LiteralPath (Join-Path $engine 'LICENSE.md') -Destination (Join-Path $licenses 'ReShade-BSD-3-Clause.txt') -Force
Copy-Item -LiteralPath (Join-Path $engine 'deps/imgui/LICENSE.txt') -Destination (Join-Path $licenses 'DearImGui-MIT.txt') -Force
Copy-Item -LiteralPath (Join-Path $detours 'LICENSE.md') -Destination (Join-Path $licenses 'Microsoft-Detours-MIT.txt') -Force
$dependencyLicenses=@{
 'deps/vma/LICENSE.txt'='VulkanMemoryAllocator-MIT.txt';'deps/minhook/LICENSE.txt'='MinHook-BSD.txt';
 'deps/utfcpp/LICENSE'='UTF8-CPP.txt';'deps/glad/LICENSE'='glad.txt';'deps/stb/LICENSE'='stb.txt';
 'deps/spirv/LICENSE'='SPIRV.txt';'deps/openvr/LICENSE'='OpenVR.txt';'deps/openxr/LICENSE'='OpenXR.txt';
 'deps/openxr/COPYING.adoc'='OpenXR-COPYING.txt';'deps/jxl_simple_lossless/LICENSE'='libjxl-BSD.txt';
 'deps/d3d12/LICENSE'='DirectX-Headers-MIT.txt';'deps/d3d12on7/LICENSE.txt'='D3D12on7.txt';'deps/d3d911on12/LICENSE.txt'='D3D9On12.txt'
}
foreach($entry in $dependencyLicenses.GetEnumerator()) {Copy-Item -LiteralPath (Join-Path $engine $entry.Key) -Destination (Join-Path $licenses $entry.Value) -Force}
Copy-Item -LiteralPath (Join-Path $engine 'deps/openxr/LICENSES/Apache-2.0.txt') -Destination (Join-Path $licenses 'Apache-2.0.txt') -Force
Copy-Item -LiteralPath (Join-Path $engine 'deps/openxr/LICENSES/OFL-1.1-RFN.txt') -Destination (Join-Path $licenses 'SIL-OFL-1.1.txt') -Force
$fpng=[IO.File]::ReadAllText((Join-Path $engine 'deps/fpng/src/fpng.cpp'))
$licenseStart=$fpng.LastIndexOf('/*'); if($licenseStart -lt 0){throw 'Missing fpng license'}
[IO.File]::WriteAllText((Join-Path $licenses 'fpng-Unlicense.txt'),$fpng.Substring($licenseStart),[Text.UTF8Encoding]::new($false))
$about=[IO.File]::ReadAllText((Join-Path $engine 'source/runtime_gui.cpp'))
$aboutStart=$about.IndexOf('if (ImGui::CollapsingHeader("ImGuiColorTextEdit"))');$aboutEnd=$about.IndexOf('ImGui::PopTextWrapPos();',$aboutStart)
if($aboutStart -lt 0 -or $aboutEnd -lt $aboutStart){throw 'Missing engine embedded notices'}
[IO.File]::WriteAllText((Join-Path $licenses 'ReShade-Embedded-Notices.txt'),$about.Substring($aboutStart,$aboutEnd-$aboutStart),[Text.UTF8Encoding]::new($false))
$manifestSources=@(Get-ChildItem -LiteralPath $source -File | Where-Object Extension -in @('.cpp','.hpp') | ForEach-Object { @{path=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
$files=@(Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object { $_.Name -ne 'payload.json' -and $_.Extension -notin @('.lib','.exp') } | ForEach-Object {
 @{path=[IO.Path]::GetRelativePath($payload,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
})
@{schema=1;engine='ReShade 6.8.0';engineCommit='18deaa52de0c425a78b329e9cb3c497281cd00ec';launchAdapter='Microsoft Detours 4.0.1';
 launchCommit='e4bfd6b03e50de46b47abfbd1e46b384f0c5f833';builtFromSource=$true;readingFontRoles=1;engineGuiSha256=(Get-FileHash -LiteralPath $guiPath).Hash;nativeSources=$manifestSources;files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'payload.json')
@{payload=$payload;files=$files.Count} | ConvertTo-Json
