param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $PayloadArchive,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $CompilerPath
)

$ErrorActionPreference = 'Stop'
$expectedArchiveSha256 = 'a5e1966a322ec19e391a775ef1909fec32a51b36b3869215a0117fc24fcb174c'
$expectedPayloadRoot = 'RocketIDE-win-x64-1.0.0'
$iconPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\RocketIDE.App\Assets\RocketIDE.ico'
$requiredPayloadFiles = @(
    'RocketIDE.exe',
    'RocketIDE.Debugger.dll',
    'amd64\EngHost.exe',
    'RocketIDE.runtimeconfig.json'
)

$archivePath = (Resolve-Path -LiteralPath $PayloadArchive).Path
$compiler = (Resolve-Path -LiteralPath $CompilerPath).Path
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($archiveHash -ne $expectedArchiveSha256) {
    throw "Frozen consumer archive SHA-256 mismatch. Expected $expectedArchiveSha256; received $archiveHash."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($outputPath) | Out-Null
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('RocketIDE-Installer-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$previousPayloadRoot = $env:ROCKETIDE_PAYLOAD_ROOT
$previousInstallerOutput = $env:ROCKETIDE_INSTALLER_OUTPUT
$previousWizardLogo = $env:ROCKETIDE_WIZARD_LOGO

try {
    [System.IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $tempRoot)
    $payloadRoot = Join-Path $tempRoot $expectedPayloadRoot
    if (-not (Test-Path -LiteralPath $payloadRoot -PathType Container)) {
        throw "The frozen archive does not contain the expected $expectedPayloadRoot directory."
    }

    foreach ($relativePath in $requiredPayloadFiles) {
        $requiredPath = Join-Path $payloadRoot $relativePath
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Required consumer runtime/debugger file is missing: $relativePath"
        }
    }

    Add-Type -AssemblyName System.Drawing
    $wizardLogo = Join-Path $tempRoot 'RocketIDE-Wizard.png'
    $sourceIcon = [System.Drawing.Icon]::new($iconPath, 147, 147)
    $sourceBitmap = $sourceIcon.ToBitmap()
    $logoBitmap = [System.Drawing.Bitmap]::new(147, 147, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($logoBitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($sourceBitmap, 0, 0, 147, 147)
        $logoBitmap.Save($wizardLogo, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $logoBitmap.Dispose()
        $sourceBitmap.Dispose()
        $sourceIcon.Dispose()
    }

    $env:ROCKETIDE_PAYLOAD_ROOT = $payloadRoot
    $env:ROCKETIDE_INSTALLER_OUTPUT = $outputPath
    $env:ROCKETIDE_WIZARD_LOGO = $wizardLogo
    $scriptPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'installer\RocketIDE.iss'
    & $compiler /Qp $scriptPath
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compiler failed with exit code $LASTEXITCODE."
    }

    $installerPath = Join-Path $outputPath 'RocketIDE-Setup-1.0.0.exe'
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw "Inno Setup completed without creating $installerPath."
    }

    $installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$installerPath.sha256" -Value "$installerHash  RocketIDE-Setup-1.0.0.exe" -Encoding ascii
    Write-Output "Frozen consumer archive: $archiveHash"
    Write-Output "Installer: $installerPath"
    Write-Output "Installer SHA-256: $installerHash"
}
finally {
    $env:ROCKETIDE_PAYLOAD_ROOT = $previousPayloadRoot
    $env:ROCKETIDE_INSTALLER_OUTPUT = $previousInstallerOutput
    $env:ROCKETIDE_WIZARD_LOGO = $previousWizardLogo
    $resolvedTemp = [System.IO.Path]::GetFullPath($tempRoot)
    $systemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedTemp.StartsWith($systemTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTemp).StartsWith('RocketIDE-Installer-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
