$ErrorActionPreference = 'Stop'
$inputRoot = Join-Path $PSScriptRoot '../docs/wp07-2026-09-27/transfer'
$recipe = Get-Content (Join-Path $inputRoot 'zip-recipe.json') -Raw | ConvertFrom-Json
$stage = [IO.Path]::GetFullPath('transfer-stage')
Copy-Item "$inputRoot/overrides/*" $stage -Force
$mismatches = @()
foreach ($entry in $recipe.entries) {
    $file = Join-Path $stage $entry.relative
    if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant() -ne $entry.file_sha256) {
        $mismatches += $entry.relative
    }
}
if ($mismatches.Count) { throw "CI runtime files differ from local package: $($mismatches -join ', ')" }
$output = [IO.Path]::GetFullPath('RocketIDE-win-x64-1.0.0.zip')
$stream = [IO.File]::Create($output)
try {
    foreach ($entry in $recipe.entries) {
        $prefix = [Convert]::FromBase64String($entry.prefix)
        $stream.Write($prefix)
        $data = [IO.File]::ReadAllBytes((Join-Path $stage $entry.relative))
        if ($entry.method -eq 8) {
            $memory = [IO.MemoryStream]::new()
            $deflater = [IO.Compression.DeflateStream]::new($memory, [IO.Compression.CompressionLevel]::Optimal, $true)
            $deflater.Write($data)
            $deflater.Dispose()
            $data = $memory.ToArray()
            $memory.Dispose()
        } elseif ($entry.method -ne 0) { throw 'Unsupported compression' }
        $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($data)).ToLowerInvariant()
        if ($data.Length -ne $entry.compressed_size -or $digest -ne $entry.compressed_sha256) { throw "Compression differs: $($entry.relative)" }
        $stream.Write($data)
    }
    $stream.Write([Convert]::FromBase64String($recipe.trailer))
} finally { $stream.Dispose() }
if ((Get-FileHash -LiteralPath $output).Hash.ToLowerInvariant() -ne $recipe.sha256) { throw 'Reconstructed ZIP differs' }
Write-Host "Exact ZIP reproduced: $($recipe.sha256); $($recipe.entries.Count) files"
