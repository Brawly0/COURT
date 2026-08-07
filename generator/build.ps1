$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not (Test-Path "$root\bin")) { New-Item -ItemType Directory "$root\bin" | Out-Null }
javac -d "$root\bin" (Get-ChildItem "$root\src\court\*.java" | ForEach-Object { $_.FullName })
Write-Host "built -> $root\bin"
