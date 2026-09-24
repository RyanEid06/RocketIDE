$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$candidates = @()
if (-not [string]::IsNullOrWhiteSpace($env:ROCKET_COMPILER)) {
    $candidates += $env:ROCKET_COMPILER
}

$rocketRoot = [IO.Path]::GetFullPath((Join-Path $repo '..\Rocket'))
$candidates += @(
    (Join-Path $rocketRoot 'out\build\windows-release\rocketc.exe'),
    (Join-Path $rocketRoot 'out\build\windows-debug\rocketc.exe'),
    (Join-Path $rocketRoot 'out\package\bin\rocketc.exe')
)

$packageRoot = Join-Path $rocketRoot 'out\package'
if (Test-Path -LiteralPath $packageRoot) {
    $candidates += Get-ChildItem -LiteralPath $packageRoot -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'bin\rocketc.exe' }
}

$compiler = $candidates |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) } |
    Select-Object -First 1

if ($null -eq $compiler) {
    $command = Get-Command rocketc.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $compiler = $command.Source
    }
}
if ($null -eq $compiler) {
    throw 'Authoritative rocketc.exe not found. Set ROCKET_COMPILER or build the sibling Rocket checkout.'
}

Write-Host "Using authoritative compiler: $compiler"

$temp = Join-Path ([IO.Path]::GetTempPath()) ("rocketide-wp05-snippets-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$utf8NoBom = [Text.UTF8Encoding]::new($false)

try {
    $fixtures = [ordered]@{
        'main.rocket' = @"
fn main() -> Int:
    print("Hello, Rocket!")
    return 0
"@
        'fn.rocket' = @"
fn name(value: Int) -> Int:
    return value

fn main() -> Int:
    return name(0)
"@
        'impl.rocket' = @"
struct Type:
    value: Int

impl Type:
    fn method(self: Type) -> Int:
        return 0

fn main() -> Int:
    let value = Type(0)
    return value.method()
"@
        'match.rocket' = @"
fn main() -> Int:
    let value: Option[Int] = Some(1)
    match value:
        case Some(value):
            return value
        case None:
            return 0
"@
        'testmain.rocket' = @"
fn main() -> Int:
    if true:
        return 0
    return 1
"@
    }

    foreach ($entry in $fixtures.GetEnumerator()) {
        $path = Join-Path $temp $entry.Key
        [IO.File]::WriteAllText($path, $entry.Value.TrimStart("`r", "`n") + "`n", $utf8NoBom)
        Write-Host "Checking snippet fixture: $($entry.Key)"
        & $compiler check $path --message-format=json
        if ($LASTEXITCODE -ne 0) {
            throw "Rocket snippet compiler smoke failed for $($entry.Key)."
        }
    }

    Write-Host 'All five bundled Rocket snippet constructs compile successfully.' -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
