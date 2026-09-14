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

    Write-Host '== RocketIDE source ignore guard =='
    $requiredSourcePaths = @(
        'src/RocketIDE.Core/Recovery/RecoverySnapshot.cs',
        'src/RocketIDE.Infrastructure/Recovery/JsonRecoveryStore.cs',
        'tests/RocketIDE.Core.Tests/Recovery/RecoveryModelTests.cs',
        'tests/RocketIDE.Infrastructure.Tests/Recovery/JsonRecoveryStoreTests.cs'
    )
    foreach ($requiredSourcePath in $requiredSourcePaths) {
        & git check-ignore --no-index --quiet -- $requiredSourcePath
        if ($LASTEXITCODE -eq 0) {
            throw "Required source/test path is ignored by .gitignore: $requiredSourcePath"
        }
        if ($LASTEXITCODE -ne 1) {
            throw "git check-ignore failed for '$requiredSourcePath' with exit code $LASTEXITCODE."
        }
    }

    Write-Host '== RocketIDE clean generated build state =='
    # Patch ZIP extraction can preserve source timestamps older than an existing incremental build.
    # Clean only each MSBuild project's own bin/obj directories so verification cannot reuse stale
    # outputs while still leaving any intentionally named test-fixture directories alone.
    $projectDirectories = Get-ChildItem -Path (Join-Path $repo 'src'), (Join-Path $repo 'tests') -Filter '*.csproj' -File -Recurse |
        ForEach-Object { $_.Directory.FullName } |
        Sort-Object -Unique
    foreach ($projectDirectory in $projectDirectories) {
        foreach ($generatedName in @('bin', 'obj')) {
            $generatedPath = Join-Path $projectDirectory $generatedName
            if (Test-Path -LiteralPath $generatedPath) {
                Remove-Item -LiteralPath $generatedPath -Recurse -Force
            }
        }
    }

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

    $debuggerAssembly = Join-Path $publish 'RocketIDE.Debugger.dll'
    if (-not (Test-Path -LiteralPath $debuggerAssembly)) {
        throw "Publish completed without expected debugger assembly: $debuggerAssembly"
    }
    if ((Get-Item -LiteralPath $debuggerAssembly).Length -le 0) {
        throw "Published debugger assembly is empty: $debuggerAssembly"
    }

    $engHosts = @(Get-ChildItem -LiteralPath $publish -Filter 'EngHost.exe' -File -Recurse)
    if ($engHosts.Count -eq 0) {
        throw 'Publish completed without DbgX EngHost.exe.'
    }
    $x64EngHost = $engHosts | Where-Object { $_.Directory.Name -in @('x64', 'amd64') } | Select-Object -First 1
    if ($null -eq $x64EngHost) {
        throw 'Publish completed without x64/amd64 EngHost.exe required by the win-x64 debugger.'
    }

    Write-Host "Published $($exeInfo.FullName) ($($exeInfo.Length) bytes)"
    Write-Host "Debugger engine host $($x64EngHost.FullName)"
    Write-Host 'RocketIDE verification PASSED.' -ForegroundColor Green
}
finally {
    Pop-Location
}
