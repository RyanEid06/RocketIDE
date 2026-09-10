$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    Write-Host '== RocketIDE SDK guard =='
    $dotnetVersion = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($dotnetVersion)) {
        throw 'The .NET SDK is not available on PATH.'
    }

    $dotnetMajor = [int]($dotnetVersion.Split('.')[0])
    if ($dotnetMajor -ne 10) {
        throw "RocketIDE requires .NET SDK 10.x. Found $dotnetVersion."
    }
    Write-Host "Using .NET SDK $dotnetVersion"

    Write-Host '== RocketIDE restore =='
    dotnet restore .\RocketIDE.sln
    if ($LASTEXITCODE -ne 0) { throw 'Solution restore failed.' }

    Write-Host '== RocketIDE win-x64 publish restore =='
    dotnet restore .\src\RocketIDE.App\RocketIDE.App.csproj -r win-x64
    if ($LASTEXITCODE -ne 0) { throw 'win-x64 publish restore failed.' }

    Write-Host '== RocketIDE build =='
    dotnet build .\RocketIDE.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }

    Write-Host '== RocketIDE tests =='
    dotnet test .\RocketIDE.sln -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Test run failed.' }

    Write-Host '== RocketIDE publish smoke =='
    $publish = Join-Path $repo 'artifacts\verify-win-x64'
    if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

    dotnet publish .\src\RocketIDE.App\RocketIDE.App.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        --no-restore `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'win-x64 publish failed.' }

    $exe = Join-Path $publish 'RocketIDE.exe'
    if (-not (Test-Path $exe)) {
        throw "Publish completed without expected $exe"
    }

    $exeInfo = Get-Item $exe
    if ($exeInfo.Length -le 0) {
        throw "Published executable is empty: $exe"
    }

    Write-Host "Published $($exeInfo.FullName) ($($exeInfo.Length) bytes)"
    Write-Host 'RocketIDE verification PASSED.' -ForegroundColor Green
}
finally {
    Pop-Location
}
