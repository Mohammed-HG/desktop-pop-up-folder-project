$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $root 'DesktopFolders.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
dotnet test (Join-Path $root 'DesktopFolders.sln') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
