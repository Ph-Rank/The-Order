param([Parameter(Mandatory)][string]$OutputDirectory, [switch]$UpdatePinnedBinary)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd('\')
$output = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
if (!$output.StartsWith((Join-Path $repo '.artifacts') + '\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)) { throw 'Fresh workspace artifact output required' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$cmake = Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
if (!(Test-Path -LiteralPath $cmake)) { throw 'Existing Visual Studio CMake and x64 C++ tools required; no automatic installation' }
New-Item -ItemType Directory -Path $output | Out-Null
$downloads = @(
    @{file='leveldb-1.23.zip';url='https://codeload.github.com/google/leveldb/zip/refs/tags/1.23';sha='a6fa7eebd11de709c46bf1501600ed98bf95439d6967963606cc964931ce906f'},
    @{file='snappy-1.2.2.zip';url='https://codeload.github.com/google/snappy/zip/refs/tags/1.2.2';sha='89c009c9c661f466aa1c4fd866ca29c222e5b1b7d73d2beadb44493c3d0d8476'})
foreach ($download in $downloads) {
    $archive = Join-Path $output $download.file
    Invoke-WebRequest -Uri $download.url -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() -ne $download.sha) { throw 'Upstream source digest changed; not extracted or executed' }
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $output)
}
$cmakeSource = @'
cmake_minimum_required(VERSION 3.20)
project(TheOrderLevelDB LANGUAGES C CXX)
set(CMAKE_POLICY_DEFAULT_CMP0091 NEW)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
set(CMAKE_CXX_STANDARD 17)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(SNAPPY_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(SNAPPY_BUILD_BENCHMARKS OFF CACHE BOOL "" FORCE)
set(SNAPPY_INSTALL OFF CACHE BOOL "" FORCE)
set(SNAPPY_REQUIRE_AVX OFF CACHE BOOL "" FORCE)
set(SNAPPY_REQUIRE_AVX2 OFF CACHE BOOL "" FORCE)
set(SNAPPY_HAVE_SSSE3 OFF CACHE INTERNAL "" FORCE)
set(SNAPPY_HAVE_BMI2 OFF CACHE INTERNAL "" FORCE)
set(SNAPPY_HAVE_X86_CRC32 OFF CACHE INTERNAL "" FORCE)
add_subdirectory(snappy-1.2.2)
include_directories(${CMAKE_CURRENT_SOURCE_DIR}/snappy-1.2.2 ${CMAKE_CURRENT_BINARY_DIR}/snappy-1.2.2)
set(BUILD_SHARED_LIBS ON CACHE BOOL "" FORCE)
set(HAVE_SNAPPY ON CACHE INTERNAL "" FORCE)
set(LEVELDB_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(LEVELDB_BUILD_BENCHMARKS OFF CACHE BOOL "" FORCE)
set(LEVELDB_INSTALL OFF CACHE BOOL "" FORCE)
add_subdirectory(leveldb-1.23)
'@
[IO.File]::WriteAllText((Join-Path $output 'CMakeLists.txt'), $cmakeSource, [Text.UTF8Encoding]::new($false))
$build = Join-Path $output 'build'
# Use the installed Visual Studio generator; all state stays in the fresh output.
& $cmake -S $output -B $build -A x64 '-DCMAKE_POLICY_VERSION_MINIMUM=3.5' > (Join-Path $output 'configure.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Native engine configure failed; inspect local configure.log' }
& $cmake --build $build --config Release --target leveldb --parallel 4 > (Join-Path $output 'build.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Native engine build failed; inspect local build.log' }
$binary = Join-Path $build 'leveldb-1.23/Release/leveldb.dll'
$sha = (Get-FileHash -LiteralPath $binary).Hash.ToLowerInvariant()
if ($UpdatePinnedBinary) {
    $target = Join-Path $repo 'native/TheOrder.Infrastructure/Migration/Native/win-x64'
    Copy-Item -LiteralPath $binary -Destination (Join-Path $target 'leveldb.dll')
    $manifestPath = Join-Path $target 'build.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.sha256 = $sha
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
}
[pscustomobject]@{binary=$binary;sha256=$sha;sourceDigestsVerified=$true;updatedPinnedBinary=[bool]$UpdatePinnedBinary;requiresSnappyDll=$false} | ConvertTo-Json
