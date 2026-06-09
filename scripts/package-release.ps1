param(
    [string]$Version = "v0.1.0",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Remove-GeneratedPath([string]$Path, [string]$AllowedRoot) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolvedPath = Resolve-FullPath $Path
    $resolvedRoot = Resolve-FullPath $AllowedRoot
    if (-not $resolvedPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside artifacts root: $resolvedPath"
    }

    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

$repoRoot = Resolve-FullPath (Join-Path $PSScriptRoot "..")
$artifactsRoot = Join-Path $repoRoot "artifacts"
$releaseName = "figma-unity-exporter-$Version"
$stageRoot = Join-Path $artifactsRoot $releaseName
$publishRoot = Join-Path $artifactsRoot "$releaseName-bridge-publish"
$zipPath = Join-Path $artifactsRoot "$releaseName.zip"
$bridgeProject = Join-Path $repoRoot "bridge\FigmaUnityBridge.csproj"

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
Remove-GeneratedPath $stageRoot $artifactsRoot
Remove-GeneratedPath $publishRoot $artifactsRoot
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stageRoot "bridge") | Out-Null

dotnet publish $bridgeProject `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishRoot

$bridgeExe = Join-Path $publishRoot "FigmaUnityBridge.exe"
$stageBridgeExe = Join-Path $stageRoot "bridge\FigmaUnityBridge.exe"
$copiedBridge = $false
for ($attempt = 1; $attempt -le 5; $attempt++) {
    try {
        Copy-Item -LiteralPath $bridgeExe -Destination $stageBridgeExe -Force -ErrorAction Stop
        $copiedBridge = $true
        break
    } catch {
        if ($attempt -eq 5) {
            throw
        }

        Start-Sleep -Seconds 1
    }
}

if (-not $copiedBridge) {
    throw "Failed to copy bridge executable."
}

Copy-Item -LiteralPath (Join-Path $repoRoot "figma-plugin") -Destination (Join-Path $stageRoot "figma-plugin") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "unity") -Destination (Join-Path $stageRoot "unity") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $stageRoot "README.md") -Force

$commit = "unknown"
try {
    $commit = (git -C $repoRoot rev-parse --short HEAD).Trim()
} catch {
    $commit = "unknown"
}

$manifest = [ordered]@{
    name = "Codex Figma Unity Exporter"
    version = $Version
    commit = $commit
    builtAtUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
    contents = @(
        "figma-plugin/manifest.json",
        "bridge/FigmaUnityBridge.exe",
        "unity/Assets/Editor",
        "unity/Assets/FigmaUnityExport"
    )
} | ConvertTo-Json -Depth 5

[System.IO.File]::WriteAllText(
    (Join-Path $stageRoot "release-manifest.json"),
    $manifest,
    [System.Text.UTF8Encoding]::new($false)
)

$installText = @'
# Quick Install

1. Import Figma plugin from `figma-plugin/manifest.json`.
2. Copy `unity/Assets/Editor` and `unity/Assets/FigmaUnityExport` into your Unity project root.
3. Start the bridge:

```powershell
.\bridge\FigmaUnityBridge.exe --project <UnityProjectRoot>
```

Bridge health check:

```text
http://localhost:17890/health
```
'@

[System.IO.File]::WriteAllText(
    (Join-Path $stageRoot "INSTALL.md"),
    $installText,
    [System.Text.UTF8Encoding]::new($false)
)

$compressed = $false
for ($attempt = 1; $attempt -le 5; $attempt++) {
    try {
        if (Test-Path -LiteralPath $zipPath) {
            Remove-Item -LiteralPath $zipPath -Force
        }

        Compress-Archive -Path (Join-Path $stageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal -ErrorAction Stop
        $compressed = $true
        break
    } catch {
        if ($attempt -eq 5) {
            throw
        }

        Start-Sleep -Seconds 1
    }
}

if (-not $compressed) {
    throw "Failed to create release zip."
}

Write-Host "Release package created:"
Write-Host $zipPath
