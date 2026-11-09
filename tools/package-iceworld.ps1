param(
    [Parameter(Mandatory = $true)][string]$Destination,
    [Parameter(Mandatory = $true)][string]$NativeQaDirectory
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$destinationPath = [System.IO.Path]::GetFullPath($Destination)
$qaPath = (Resolve-Path -LiteralPath $NativeQaDirectory).Path
$package = Join-Path $destinationPath 'Miki-IceWorld-Examples'
if (Test-Path -LiteralPath $package) { throw 'Use a new destination directory for a reproducible package.' }
& dotnet build (Join-Path $repository 'src/Miki.IceWorld.Grasshopper') --configuration Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Grasshopper build failed.' }
$binary = Join-Path $repository 'src/Miki.IceWorld.Grasshopper/bin/Release/net8.0-windows'
foreach ($directory in @('plugin', 'examples/iceworld', 'models', 'previews', 'docs')) {
    New-Item -ItemType Directory -Path (Join-Path $package $directory) -Force | Out-Null
}
foreach ($file in @('Miki.IceWorld.Grasshopper.gha', 'Miki.IceWorld.dll', 'execution.json')) {
    Copy-Item -LiteralPath (Join-Path $binary $file) -Destination (Join-Path $package 'plugin')
}
Copy-Item -Path (Join-Path $repository 'examples/iceworld/*.ghx') -Destination (Join-Path $package 'examples/iceworld')
Copy-Item -Path (Join-Path $repository 'examples/iceworld/*.json') -Destination (Join-Path $package 'examples/iceworld')
Copy-Item -LiteralPath (Join-Path $repository 'examples/iceworld/README.md') -Destination (Join-Path $package 'README.zh-CN.md')
Copy-Item -LiteralPath (Join-Path $repository 'examples/index.json') -Destination (Join-Path $package 'examples/index.json')
foreach ($file in @('warehouse-index.md', 'approval-rules.md', 'iceworld-validation.md', 'iceworld-native-validation.json')) {
    Copy-Item -LiteralPath (Join-Path $repository "docs/$file") -Destination (Join-Path $package 'docs')
}
foreach ($kind in @('straight-stair', 'switchback-stair', 'platform-railing', 'queue-barriers', 'curved-ice-wall', 'spiral-ornament')) {
    Copy-Item -LiteralPath (Join-Path $qaPath "$kind.3dm") -Destination (Join-Path $package 'models')
    Copy-Item -LiteralPath (Join-Path $qaPath "$kind.png") -Destination (Join-Path $package 'previews')
}
$manifest = Get-ChildItem -LiteralPath $package -Recurse -File | ForEach-Object {
    [PSCustomObject]@{ path = [System.IO.Path]::GetRelativePath($package, $_.FullName).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $package 'manifest.json') -Encoding utf8
Compress-Archive -LiteralPath $package -DestinationPath (Join-Path $destinationPath 'Miki-IceWorld-Examples.zip')
Write-Output (Join-Path $destinationPath 'Miki-IceWorld-Examples.zip')
