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
$dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $dotnet) { $dotnet = Get-Command (Join-Path $env:ProgramFiles 'dotnet/dotnet.exe') -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1 }
if (-not $dotnet) { throw 'dotnet executable was not found. Install the .NET 8 SDK or add dotnet to PATH.' }
$buildStart = New-Object System.Diagnostics.ProcessStartInfo
$buildStart.FileName = $dotnet.Source
$buildStart.Arguments = 'build "' + (Join-Path $repository 'src/Miki.IceWorld.Grasshopper') + '" --configuration Release --nologo'
$buildStart.WorkingDirectory = $repository
$buildStart.UseShellExecute = $false
$buildStart.RedirectStandardOutput = $true
$buildStart.RedirectStandardError = $true
$buildProcess = New-Object System.Diagnostics.Process
$buildProcess.StartInfo = $buildStart
if (-not $buildProcess.Start()) { throw 'Could not start the Grasshopper build.' }
$buildOutput = $buildProcess.StandardOutput.ReadToEnd()
$buildErrors = $buildProcess.StandardError.ReadToEnd()
$buildProcess.WaitForExit()
Write-Output $buildOutput
Write-Error $buildErrors -ErrorAction SilentlyContinue
if ($buildProcess.ExitCode -ne 0) { throw "Grasshopper build failed with exit code $($buildProcess.ExitCode)." }
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
foreach ($file in @('warehouse-index.md', 'approval-rules.md', 'iceworld-standard-baseline.md', 'iceworld-validation.md', 'iceworld-native-validation.json')) {
    Copy-Item -LiteralPath (Join-Path $repository "docs/$file") -Destination (Join-Path $package 'docs')
}
Copy-Item -LiteralPath (Join-Path $repository 'policies/iceworld-standard-baseline.json') -Destination (Join-Path $package 'docs/iceworld-standard-baseline.json')
foreach ($kind in @('straight-stair', 'switchback-stair', 'platform-railing', 'queue-barriers', 'curved-ice-wall', 'spiral-ornament')) {
    Copy-Item -LiteralPath (Join-Path $qaPath "$kind.3dm") -Destination (Join-Path $package 'models')
    Copy-Item -LiteralPath (Join-Path $qaPath "$kind.png") -Destination (Join-Path $package 'previews')
}
$manifest = Get-ChildItem -LiteralPath $package -Recurse -File | ForEach-Object {
    $prefix = $package.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $relative = $_.FullName.Substring($prefix.Length).Replace('\', '/')
    [PSCustomObject]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $package 'manifest.json') -Encoding utf8
Compress-Archive -LiteralPath $package -DestinationPath (Join-Path $destinationPath 'Miki-IceWorld-Examples.zip')
Write-Output (Join-Path $destinationPath 'Miki-IceWorld-Examples.zip')
