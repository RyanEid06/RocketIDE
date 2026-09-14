param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputRoot = '',
    [string]$Version = '',
    [switch]$KeepSymbols
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
        $OutputRoot = Join-Path $repo 'artifacts\package-win-x64'
    } elseif (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
        $OutputRoot = Join-Path $repo $OutputRoot
    }

    $OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
    $versionLabel = if ([string]::IsNullOrWhiteSpace($Version)) { 'local' } else { $Version.Trim() }
    $safeVersion = ($versionLabel -replace '[^A-Za-z0-9._-]', '-')
    $folderName = "RocketIDE-win-x64-$safeVersion"
    $stageRoot = Join-Path $OutputRoot 'stage'
    $packageRoot = Join-Path $stageRoot $folderName
    $publishRoot = $packageRoot
    $zipPath = Join-Path $OutputRoot "$folderName.zip"
    $hashPath = "$zipPath.sha256"

    if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    if (Test-Path -LiteralPath $hashPath) { Remove-Item -LiteralPath $hashPath -Force }
    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

    $publishArguments = @(
        'publish', '.\src\RocketIDE.App\RocketIDE.App.csproj',
        '-c', $Configuration,
        '-r', 'win-x64',
        '--self-contained', 'true',
        '--no-restore',
        "-p:RocketIDEInformationalVersion=$versionLabel",
        '-o', $publishRoot
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw 'RocketIDE publish failed.' }

    $exe = Join-Path $packageRoot 'RocketIDE.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "Published executable not found: $exe" }
    if ((Get-Item -LiteralPath $exe).Length -le 0) { throw "Published executable is empty: $exe" }

    $debuggerAssembly = Join-Path $packageRoot 'RocketIDE.Debugger.dll'
    if (-not (Test-Path -LiteralPath $debuggerAssembly)) { throw "Debugger assembly not found: $debuggerAssembly" }
    if ((Get-Item -LiteralPath $debuggerAssembly).Length -le 0) { throw "Debugger assembly is empty: $debuggerAssembly" }

    $engHosts = @(Get-ChildItem -LiteralPath $packageRoot -Filter 'EngHost.exe' -File -Recurse)
    if ($engHosts.Count -eq 0) { throw 'DbgX EngHost.exe was not published. The portable debugger cannot start without it.' }
    if (-not ($engHosts | Where-Object { $_.Directory.Name -in @('x64', 'amd64') })) {
        throw 'DbgX x64/amd64 EngHost.exe was not published. The win-x64 portable debugger cannot start without it.'
    }
    if (-not $KeepSymbols) {
        Get-ChildItem -LiteralPath $packageRoot -Filter '*.pdb' -File -Recurse | Remove-Item -Force
    }

    $readme = Join-Path $packageRoot 'README.txt'
    @(
        "RocketIDE $versionLabel",
        '',
        'Run RocketIDE.exe. Configure the Rocket SDK from Tools > Rocket SDK Settings.',
        'User settings, logs, and recovery snapshots are stored under LocalApplicationData.',
        'The portable package includes RocketIDE''s Microsoft DbgX/DbgEng native debugger engine.',
        'This portable package does not bundle a Rocket SDK.'
    ) | Set-Content -LiteralPath $readme -Encoding UTF8

    New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
    Compress-Archive -Path $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath $hashPath -Encoding ASCII

    Write-Host "Created $zipPath"
    Write-Host "SHA-256 $hash"
}
finally {
    Pop-Location
}
