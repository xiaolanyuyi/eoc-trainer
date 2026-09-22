# Publishes the trainer as a single executable.
#
#   powershell -File tools\publish.ps1              # needs .NET 10 runtime on the target
#   powershell -File tools\publish.ps1 -SelfContained # fully standalone (~70 MB)
param(
    [switch]$SelfContained,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\EocTrainer\EocTrainer.csproj"
$output = if ($SelfContained) { Join-Path $root "dist\standalone" } else { Join-Path $root "dist" }

if (Test-Path $output) { Remove-Item $output -Recurse -Force }

$arguments = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", "win-x64",
    "-p:PublishSingleFile=true",
    "-o", $output
)

if ($SelfContained) {
    $arguments += "--self-contained", "true"
    $arguments += "-p:IncludeNativeLibrariesForSelfExtract=true"
} else {
    $arguments += "--self-contained", "false"
}

Write-Host "dotnet $($arguments -join ' ')"
& dotnet @arguments

Write-Host ""
Write-Host "输出目录: $output"
Get-ChildItem $output | Select-Object Name, Length
