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
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        "-p:RocketIDEInformationalVersion=$versionLabel",
        '-o', $publishRoot
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw 'RocketIDE publish failed.' }

    $exe = Join-Path $packageRoot 'RocketIDE.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "Published executable not found: $exe" }
    if (-not $KeepSymbols) {
        Get-ChildItem -LiteralPath $packageRoot -Filter '*.pdb' -File -Recurse | Remove-Item -Force
    }

    $readme = Join-Path $packageRoot 'README.txt'
    @(
        "RocketIDE $versionLabel",
        '',
        'Run RocketIDE.exe. Configure the Rocket SDK from Tools > Rocket SDK Settings.',
        'User settings, logs, and recovery snapshots are stored under LocalApplicationData.',
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
