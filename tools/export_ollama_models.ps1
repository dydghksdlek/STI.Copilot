# Export selected Ollama models (manifests + referenced blobs) for offline transfer.
# Run on the PC that HAS the models. Edit $models to match 'ollama list' exactly, set $dst, then:
#   powershell -ExecutionPolicy Bypass -File export_ollama_models.ps1
# The script prints installed models first so you can match names/tags.

# ===== EDIT THESE (must match names shown in the installed list below) =====
$models = @(
    'bge-m3:latest',
    'qwen2.5:14b',
    'qwen2.5:7b'
)
$dst = 'D:\ollama_export'
# ==========================================================================

$ErrorActionPreference = 'Stop'
$src = Join-Path $env:USERPROFILE '.ollama\models'
if ($env:OLLAMA_MODELS) { $src = $env:OLLAMA_MODELS }
$manRoot  = Join-Path $src 'manifests'
$blobRoot = Join-Path $src 'blobs'
if (-not (Test-Path $manRoot)) { Write-Output "manifests not found: $manRoot"; exit 1 }

# Show installed models so you can match exact repo:tag
Write-Output "=== installed models (repo:tag) ==="
Get-ChildItem -Path $manRoot -Recurse -File | ForEach-Object {
    $p = $_.FullName.Substring($manRoot.Length).TrimStart('\') -replace '\\', '/'
    $seg = $p -split '/'
    if ($seg.Count -ge 2) { Write-Output ($seg[-2] + ':' + $seg[-1]) }
} | Sort-Object -Unique
Write-Output "==================================="

$total = 0
$copied = @{}
foreach ($m in $models) {
    $parts = $m -split ':', 2
    $repo = $parts[0]
    $tag = 'latest'; if ($parts.Count -gt 1) { $tag = $parts[1] }
    $suffix = '\' + ($repo -replace '/', '\') + '\' + $tag

    $man = Get-ChildItem -Path $manRoot -Recurse -File |
           Where-Object { $_.FullName -like "*$suffix" } | Select-Object -First 1
    if (-not $man) { Write-Output "[MISSING] $m  (name/tag not found - check installed list above)"; continue }

    $rel = $man.FullName.Substring($src.Length).TrimStart('\')
    $destMan = Join-Path $dst $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $destMan) | Out-Null
    Copy-Item $man.FullName $destMan -Force

    $json = Get-Content $man.FullName -Raw | ConvertFrom-Json
    $digests = @()
    if ($json.config.digest) { $digests += $json.config.digest }
    foreach ($l in $json.layers) { $digests += $l.digest }

    foreach ($d in $digests) {
        $blobName = 'sha256-' + ($d -replace '^sha256:', '')
        $srcBlob = Join-Path $blobRoot $blobName
        if (-not (Test-Path $srcBlob)) { Write-Output "  [!] blob missing: $blobName"; continue }
        $destBlob = Join-Path $dst ('blobs\' + $blobName)
        New-Item -ItemType Directory -Force -Path (Split-Path $destBlob) | Out-Null
        if (-not $copied.ContainsKey($blobName)) {
            Copy-Item $srcBlob $destBlob -Force
            $total += (Get-Item $srcBlob).Length
            $copied[$blobName] = $true
        }
    }
    Write-Output "[OK] $m"
}
Write-Output ("Total ~ {0} GB  ->  {1}" -f [math]::Round($total / 1GB, 2), $dst)
