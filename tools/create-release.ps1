# Creates the GitHub release and uploads the build artifacts.
# ASCII-only file (Windows PowerShell 5.1 reads BOM-less UTF-8 as ANSI), so the
# release notes come from a UTF-8 text file passed in as an argument.
#
# Usage:
#   $env:GH_TOKEN = '<token with repo scope>'
#   powershell -File tools\create-release.ps1 -Owner u -Repo r -Tag v0.1.0 `
#       -Title 'EoC Trainer v0.1.0' -NotesFile notes.md `
#       -Assets 'dist\EocTrainer-v0.1.0.exe','dist\EocTrainer-v0.1.0-standalone.zip'

param(
    [Parameter(Mandatory = $true)][string]$Owner,
    [Parameter(Mandatory = $true)][string]$Repo,
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string]$Title,
    [Parameter(Mandatory = $true)][string]$NotesFile,
    [string[]]$Assets = @()
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$token = $env:GH_TOKEN
if (-not $token) { throw 'set GH_TOKEN first' }

$api = "https://api.github.com/repos/$Owner/$Repo"
$headers = @{
    Authorization = "token $token"
    'User-Agent'  = 'eoc-trainer'
    Accept        = 'application/vnd.github+json'
}

$notes = [System.IO.File]::ReadAllText((Resolve-Path $NotesFile), [System.Text.Encoding]::UTF8)

# "-File script.ps1 -Assets a,b" arrives as one string, so split here.
$Assets = $Assets | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }

$payload = @{
    tag_name         = $Tag
    target_commitish = 'main'
    name             = $Title
    body             = $notes
    draft            = $false
    prerelease       = $false
}

Write-Output "creating release $Tag ..."
$json = $payload | ConvertTo-Json -Depth 6 -Compress

$release = $null
try {
    $release = Invoke-RestMethod -Uri "$api/releases/tags/$Tag" -Headers $headers
    Write-Output "release already exists, reusing id=$($release.id)"
} catch {
    $release = $null
}

if (-not $release) {
    try {
        $release = Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers $headers `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($json)) -ContentType 'application/json; charset=utf-8'
        Write-Output "release: $($release.html_url)  id=$($release.id)"
    } catch {
        Write-Output "create failed: $($_.Exception.Message)"
        $release = Invoke-RestMethod -Uri "$api/releases/tags/$Tag" -Headers $headers
        Write-Output "reusing release id=$($release.id)"
    }
}

foreach ($asset in $Assets) {
    if (-not (Test-Path $asset)) { Write-Output "missing asset: $asset"; continue }
    $name = Split-Path $asset -Leaf
    $size = [math]::Round((Get-Item $asset).Length / 1MB, 2)
    Write-Output "uploading $name ($size MB) ..."

    $uploadUrl = "https://uploads.github.com/repos/$Owner/$Repo/releases/$($release.id)/assets?name=$name"
    try {
        $result = Invoke-RestMethod -Method Post -Uri $uploadUrl -Headers $headers -InFile $asset `
            -ContentType 'application/octet-stream' -TimeoutSec 1800
        Write-Output "  ok: $($result.browser_download_url) ($([math]::Round($result.size / 1MB, 2)) MB)"
    } catch {
        Write-Output "  FAILED: $($_.Exception.Message)"
    }
}

Write-Output "done"
