param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectFile,

    [Parameter(Mandatory = $true)]
    [string]$ContentDirectory,

    [Parameter(Mandatory = $true)]
    [string]$NativeRuntimeDirectory,

    [Parameter(Mandatory = $true)]
    [string]$FontFile,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [string]$GameName,

    [string]$RuntimeIdentifier = 'win-x64',

    [ValidateSet('Release', 'Debug')]
    [string]$NativeConfiguration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-RelativeFileSystemPath {
    param(
        [string]$BaseDirectory,
        [string]$TargetPath
    )

    $basePath = [System.IO.Path]::GetFullPath($BaseDirectory)
    if (-not $basePath.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString(), [StringComparison]::Ordinal)) {
        $basePath += [System.IO.Path]::DirectorySeparatorChar
    }
    $targetFullPath = [System.IO.Path]::GetFullPath($TargetPath)
    $baseUri = New-Object System.Uri($basePath)
    $targetUri = New-Object System.Uri($targetFullPath)
    [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace(
        '/',
        [System.IO.Path]::DirectorySeparatorChar)
}

function Resolve-ContentAsset {
    param(
        [string]$AssetPath,
        [string]$Context
    )

    $resolvedAsset = if ([System.IO.Path]::IsPathRooted($AssetPath)) {
        [System.IO.Path]::GetFullPath($AssetPath)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $projectDirectory $AssetPath))
    }
    if (-not $resolvedAsset.StartsWith($contentPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Asset '$AssetPath' for $Context is outside ContentDirectory."
    }
    if (-not (Test-Path -LiteralPath $resolvedAsset -PathType Leaf)) {
        throw "Asset '$AssetPath' for $Context does not exist."
    }

    return $resolvedAsset
}

$resolvedContent = (Resolve-Path -LiteralPath $ContentDirectory).Path
$resolvedProject = (Resolve-Path -LiteralPath $ProjectFile).Path
$resolvedNative = (Resolve-Path -LiteralPath $NativeRuntimeDirectory).Path
$resolvedFont = (Resolve-Path -LiteralPath $FontFile).Path
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$projectRelativePath = Get-RelativeFileSystemPath $resolvedContent $resolvedProject

if ($projectRelativePath -eq '..' -or $projectRelativePath.StartsWith("..$([System.IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal) -or [System.IO.Path]::IsPathRooted($projectRelativePath)) {
    throw 'ProjectFile must be inside ContentDirectory.'
}
if ([System.IO.Path]::GetExtension($resolvedProject) -ne '.novel') {
    throw 'ProjectFile must use the .novel extension.'
}
if ($RuntimeIdentifier -ne 'win-x64') {
    throw 'The native SFML runtime packaging currently supports win-x64 only.'
}

$nativeCore = Join-Path $resolvedNative 'EngineCore.dll'
if (-not (Test-Path -LiteralPath $nativeCore -PathType Leaf)) {
    throw "Native runtime does not contain EngineCore.dll: $resolvedNative"
}
$allSfmlLibraries = Get-ChildItem -LiteralPath $resolvedNative -File -Filter 'sfml-*-3.dll'
$sfmlLibraries = if ($NativeConfiguration -eq 'Debug') {
    @($allSfmlLibraries | Where-Object { $_.Name -match '-d-3\.dll$' })
} else {
    @($allSfmlLibraries | Where-Object { $_.Name -notmatch '-d-3\.dll$' })
}
$sfmlSuffix = if ($NativeConfiguration -eq 'Debug') { '-d-3.dll' } else { '-3.dll' }
foreach ($requiredComponent in @('audio', 'graphics', 'system', 'window')) {
    $requiredLibrary = "sfml-$requiredComponent$sfmlSuffix"
    if (-not ($sfmlLibraries | Where-Object Name -EQ $requiredLibrary)) {
        throw "Native runtime is missing $requiredLibrary for $NativeConfiguration EngineCore.dll."
    }
}
if ($NativeConfiguration -eq 'Debug') {
    Write-Warning 'Packaging a Debug native engine. Use a matching Release EngineCore.dll and SFML runtime for distribution.'
}

$projectJson = Get-Content -LiteralPath $resolvedProject -Raw -Encoding UTF8 | ConvertFrom-Json
if ($projectJson -is [System.Array]) {
    $projectScenes = @($projectJson)
} elseif ($projectJson.formatVersion -eq 1 -and $null -ne $projectJson.scenes) {
    $projectScenes = @($projectJson.scenes)
} else {
    throw 'Unsupported project document. Expected a legacy scene array or formatVersion 1 document.'
}
if ($projectScenes.Count -eq 0) {
    throw 'The project has no scenes.'
}

$projectDirectory = Split-Path -Parent $resolvedProject
$contentPrefix = $resolvedContent.TrimEnd('\') + '\'
foreach ($scene in $projectScenes) {
    foreach ($propertyName in @('background', 'characterSprite', 'music')) {
        $property = $scene.PSObject.Properties[$propertyName]
        if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            continue
        }

        $assetPath = [string]$property.Value
        $resolvedAsset = Resolve-ContentAsset $assetPath "scene '$($scene.name)'"
        $property.Value = Get-RelativeFileSystemPath $projectDirectory $resolvedAsset
    }

    $commandProperty = $scene.PSObject.Properties['systemCommand']
    if ($null -ne $commandProperty -and -not [string]::IsNullOrWhiteSpace([string]$commandProperty.Value)) {
        $commandMatch = [regex]::Match(
            [string]$commandProperty.Value,
            '^\s*(?<command>PlayMusic|PlaySound)\s*:\s*(?<path>.+?)\s*$',
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($commandMatch.Success) {
            $assetPath = $commandMatch.Groups['path'].Value
            $resolvedAsset = Resolve-ContentAsset $assetPath "action in scene '$($scene.name)'"
            $relativeAsset = Get-RelativeFileSystemPath $projectDirectory $resolvedAsset
            $commandProperty.Value = "$($commandMatch.Groups['command'].Value): $relativeAsset"
        }
    }

    $luaProperty = $scene.PSObject.Properties['luaScript']
    if ($null -ne $luaProperty -and -not [string]::IsNullOrWhiteSpace([string]$luaProperty.Value)) {
        $luaAssetMatches = [regex]::Matches(
            [string]$luaProperty.Value,
            '(?i)\bplay_(?:music|sound)\s*\(\s*["''](?<path>[^"''\r\n]+)["'']')
        foreach ($luaAssetMatch in $luaAssetMatches) {
            $assetPath = $luaAssetMatch.Groups['path'].Value
            if ([System.IO.Path]::IsPathRooted($assetPath) -or
                $assetPath -match '(^|[\\/])\.\.([\\/]|$)') {
                throw "Lua asset '$assetPath' for scene '$($scene.name)' must use a project-relative path."
            }
            [void](Resolve-ContentAsset $assetPath "Lua script in scene '$($scene.name)'")
        }
    }
}

$parentDirectory = Split-Path -Parent $resolvedOutput
if (-not (Test-Path -LiteralPath $parentDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $parentDirectory | Out-Null
}
if (Test-Path -LiteralPath $resolvedOutput) {
    throw "OutputDirectory already exists; choose a new, empty output path: $resolvedOutput"
}
$releaseDirectory = Join-Path $resolvedOutput $GameName
$archivePath = Join-Path $resolvedOutput "$GameName.zip"
$stagingDirectory = Join-Path $resolvedOutput ".$GameName-staging-$([guid]::NewGuid().ToString('N'))"
$publishDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "MyGameNovella-publish-$([guid]::NewGuid().ToString('N'))"

try {
    $playerProject = Join-Path $repoRoot 'EnginePlayer\EnginePlayer.csproj'
    dotnet publish $playerProject --configuration Release --runtime $RuntimeIdentifier --self-contained true `
        --output $publishDirectory "-p:PublishSingleFile=false"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    New-Item -ItemType Directory -Path $resolvedOutput | Out-Null
    New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
    Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $stagingDirectory -Recurse -Force

    Copy-Item -LiteralPath (Join-Path $stagingDirectory 'EnginePlayer.exe') -Destination (Join-Path $stagingDirectory "$GameName.exe")
    Copy-Item -LiteralPath $nativeCore -Destination $stagingDirectory
    foreach ($library in $sfmlLibraries) {
        Copy-Item -LiteralPath $library.FullName -Destination $stagingDirectory
    }
    Copy-Item -LiteralPath $resolvedFont -Destination (Join-Path $stagingDirectory 'arial.ttf')

    $contentTarget = Join-Path $stagingDirectory 'Content'
    New-Item -ItemType Directory -Path $contentTarget | Out-Null
    $excludedDirectories = @('bin', 'obj', 'build', '.git', '.vs', '.vscode', 'node_modules', 'TestResults', 'packages')
    $excludedExtensions = @('.dll', '.exe', '.pdb', '.cache', '.log', '.user', '.suo')
    $pendingDirectories = [System.Collections.Generic.Stack[string]]::new()
    $pendingDirectories.Push($resolvedContent)
    while ($pendingDirectories.Count -gt 0) {
        $sourceDirectory = $pendingDirectories.Pop()
        $relativeDirectory = if ($sourceDirectory -eq $resolvedContent) {
            '.'
        } else {
            Get-RelativeFileSystemPath $resolvedContent $sourceDirectory
        }
        $targetDirectory = if ($relativeDirectory -eq '.') { $contentTarget } else { Join-Path $contentTarget $relativeDirectory }
        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null

        foreach ($entry in Get-ChildItem -LiteralPath $sourceDirectory -Force) {
            if ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                continue
            }
            if ($entry.PSIsContainer) {
                if ($excludedDirectories -notcontains $entry.Name) {
                    $pendingDirectories.Push($entry.FullName)
                }
            } elseif ($excludedExtensions -notcontains $entry.Extension.ToLowerInvariant() -and $entry.FullName -ne $resolvedProject) {
                Copy-Item -LiteralPath $entry.FullName -Destination $targetDirectory
            }
        }
    }

    $gameProject = Join-Path $contentTarget 'game.novel'
    $gameDocument = if ($projectJson -is [System.Array]) {
        $projectJson
    } else {
        $projectJson.scenes = $projectScenes
        $projectJson
    }
    $gameDocument | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $gameProject -Encoding UTF8

    @"
MyGameNovella game release

Run $GameName.exe to play the game.
The project and game assets are in Content\game.novel and the Content folder.
This release contains the standalone .NET player and the $NativeConfiguration native SFML engine.

Third-party components:
- MyGameNovella player and project data.
- SFML 3 native runtime; see https://www.sfml-dev.org/license.php for its license.
- MoonSharp Lua interpreter; see https://github.com/moonsharp/MoonSharp for its license.
"@ | Set-Content -LiteralPath (Join-Path $stagingDirectory 'README.txt') -Encoding UTF8

    Move-Item -LiteralPath $stagingDirectory -Destination $releaseDirectory
    Compress-Archive -Path (Join-Path $releaseDirectory '*') -DestinationPath $archivePath
    Write-Output "Game folder: $releaseDirectory"
    Write-Output "Game archive: $archivePath"
} finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }
}
