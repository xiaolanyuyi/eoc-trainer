# Pushes the current commit to GitHub through the REST API.
#
# Why not "git push": on some networks (notably behind the GFW) the git
# smart-HTTP endpoint on github.com:443 is reset while api.github.com stays
# reachable. This script uploads the objects over the API instead, which works
# wherever the API does.
#
# Note: GitHub normalises commit objects slightly (it trims leading/trailing
# whitespace off the message), so the commit SHA it creates can differ from the
# local one even though the tree - i.e. every file - is byte identical.
#
# Usage:
#   $env:GH_TOKEN = '<token with repo scope>'
#   powershell -File tools\publish-via-api.ps1 -Owner <user> -Repo <name>
#
# ASCII-only file: Windows PowerShell 5.1 reads BOM-less UTF-8 as ANSI.

param(
    [Parameter(Mandatory = $true)][string]$Owner,
    [Parameter(Mandatory = $true)][string]$Repo
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

function Invoke-Api {
    param([string]$Method, [string]$Uri, $Payload)
    if ($null -ne $Payload) {
        $json = $Payload | ConvertTo-Json -Depth 12 -Compress
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
        return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -Body $bytes -ContentType 'application/json; charset=utf-8'
    }
    return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers
}

function Get-RemoteHead {
    try {
        $ref = Invoke-Api -Method Get -Uri "$api/git/refs/heads/main"
        return $ref.object.sha
    } catch {
        return $null
    }
}

Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $head = (git rev-parse HEAD).Trim()
    $tree = (git rev-parse 'HEAD^{tree}').Trim()
    $parent = Get-RemoteHead

    Write-Output "local commit : $head"
    Write-Output "remote head  : $(if ($parent) { $parent } else { '(empty repository)' })"

    # GitHub refuses the blob API on a completely empty repository.
    if (-not $parent) {
        Write-Output "seeding the repository ..."
        $seed = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes('temporary'))
        Invoke-Api -Method Put -Uri "$api/contents/.placeholder" -Payload @{
            message = 'seed'; content = $seed; branch = 'main'
        } | Out-Null
        $parent = Get-RemoteHead
    }

    # Upload every blob straight from the local object database so the contents
    # are exactly what git stored (no line-ending surprises).
    $entries = New-Object System.Collections.Generic.List[object]
    $index = 0
    foreach ($line in @(git ls-tree -r HEAD)) {
        if ($line -notmatch '^(\d+) blob ([0-9a-f]{40})\t(.+)$') { continue }
        $mode = $Matches[1]; $blobSha = $Matches[2]; $path = $Matches[3]
        $index++

        $temp = [System.IO.Path]::GetTempFileName()
        cmd /c "git cat-file blob $blobSha > `"$temp`"" | Out-Null
        $content = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($temp))
        Remove-Item $temp -Force

        $blob = Invoke-Api -Method Post -Uri "$api/git/blobs" -Payload @{
            content = $content; encoding = 'base64'
        }
        $entries.Add(@{ path = $path; mode = $mode; type = 'blob'; sha = $blob.sha })
        if ($index % 20 -eq 0) { Write-Output "  uploaded $index files" }
    }

    Write-Output "creating tree ..."
    $remoteTree = Invoke-Api -Method Post -Uri "$api/git/trees" -Payload @{ tree = $entries }
    if ($remoteTree.sha -ne $tree) {
        Write-Output "WARNING: remote tree $($remoteTree.sha) differs from local $tree"
    } else {
        Write-Output "tree matches the local tree exactly: $tree"
    }

    Write-Output "creating commit ..."
    $message = (git log -1 --format=%B) -join "`n"
    $payload = @{
        message   = $message
        tree      = $remoteTree.sha
        author    = @{
            name  = (git log -1 --format=%an).Trim()
            email = (git log -1 --format=%ae).Trim()
            date  = (git log -1 --format=%aI).Trim()
        }
        committer = @{
            name  = (git log -1 --format=%cn).Trim()
            email = (git log -1 --format=%ce).Trim()
            date  = (git log -1 --format=%cI).Trim()
        }
    }
    if ($parent) { $payload.parents = @($parent) }

    $commit = Invoke-Api -Method Post -Uri "$api/git/commits" -Payload $payload
    Write-Output "remote commit: $($commit.sha)"

    Invoke-Api -Method Patch -Uri "$api/git/refs/heads/main" -Payload @{
        sha = $commit.sha; force = $false
    } | Out-Null
    Write-Output "branch main updated."
}
finally {
    Pop-Location
}
