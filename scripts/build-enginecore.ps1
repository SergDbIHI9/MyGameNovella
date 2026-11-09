param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$engineCoreRoot = Join-Path $repositoryRoot 'EngineCore'
$buildDirectory = Join-Path $engineCoreRoot "build\editor-msbuild-$Configuration"
$outputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

$vsWhereCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'),
    (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\Installer\vswhere.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

$visualStudioPath = $env:VSINSTALLDIR
if ($vsWhereCandidates.Count -gt 0) {
    $vsWherePath = $vsWhereCandidates | Select-Object -First 1
    $discoveredVisualStudioPath = (& $vsWherePath -latest -products '*' `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath | Select-Object -First 1)
    if (-not [string]::IsNullOrWhiteSpace($discoveredVisualStudioPath)) {
        $visualStudioPath = $discoveredVisualStudioPath
    }
}

if ([string]::IsNullOrWhiteSpace($visualStudioPath)) {
    throw 'Visual Studio C++ x64 build tools were not found. Install the Visual Studio "Desktop development with C++" workload.'
}

$vsDevCmd = Join-Path $visualStudioPath 'Common7\Tools\VsDevCmd.bat'
$ninja = Join-Path $visualStudioPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'
if (-not (Test-Path -LiteralPath $vsDevCmd)) {
    throw "Visual Studio developer environment was not found: $vsDevCmd"
}
if (-not (Test-Path -LiteralPath $ninja)) {
    throw "Visual Studio Ninja was not found: $ninja. Install the C++ CMake tools component."
}

$cmakeCommand = Get-Command 'cmake.exe' -ErrorAction SilentlyContinue
$cmakeCandidates = @(
    $(if ($cmakeCommand) { $cmakeCommand.Source }),
    (Join-Path $env:ProgramFiles 'CMake\bin\cmake.exe'),
    (Join-Path $visualStudioPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

if ($cmakeCandidates.Count -eq 0) {
    throw 'CMake was not found. Install CMake and make cmake.exe available on PATH.'
}
$cmake = $cmakeCandidates | Select-Object -First 1

$buildCommand = 'call "' + $vsDevCmd + '" -no_logo -arch=x64 -host_arch=x64 && "' +
    $cmake + '" -S "' + $engineCoreRoot + '" -B "' + $buildDirectory +
    '" -G Ninja -DCMAKE_BUILD_TYPE=' + $Configuration +
    ' -DCMAKE_MAKE_PROGRAM="' + $ninja + '" && "' + $cmake +
    '" --build "' + $buildDirectory + '" --config ' + $Configuration

& $env:ComSpec /d /s /c $buildCommand
if ($LASTEXITCODE -ne 0) {
    throw "CMake failed to build EngineCore ($Configuration), exit code $LASTEXITCODE."
}

$nativeCore = Join-Path $buildDirectory 'EngineCore.dll'
if (-not (Test-Path -LiteralPath $nativeCore)) {
    throw "CMake completed without producing EngineCore.dll: $nativeCore"
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
Copy-Item -LiteralPath $nativeCore -Destination $outputDirectory -Force

$sfmlBin = Join-Path $engineCoreRoot 'SFML\bin'
$sfmlSuffix = if ($Configuration -eq 'Debug') { '-d' } else { '' }
foreach ($component in @('graphics', 'window', 'system', 'audio')) {
    $libraryName = "sfml-$component$sfmlSuffix-3.dll"
    $libraryPath = Join-Path $sfmlBin $libraryName
    if (-not (Test-Path -LiteralPath $libraryPath)) {
        throw "The matching SFML runtime is missing: $libraryPath"
    }
    Copy-Item -LiteralPath $libraryPath -Destination $outputDirectory -Force
    Copy-Item -LiteralPath $libraryPath -Destination $outputDirectory -Force
}

$fontPath = Join-Path $env:WINDIR 'Fonts\arial.ttf'
if (Test-Path -LiteralPath $fontPath) {
    Copy-Item -LiteralPath $fontPath -Destination $outputDirectory -Force
}
else {
    Write-Warning "Arial was not found at $fontPath; EngineCore will use its Windows font fallback."
}

Write-Host "EngineCore ($Configuration) and its runtime dependencies were copied to $outputDirectory"
