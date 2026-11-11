param([switch]$Installer, [string]$Iscc)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$staging = Join-Path $artifacts ('publish-' + [guid]::NewGuid().ToString('N'))

try {
    dotnet publish (Join-Path $root 'src/DesktopFolders/DesktopFolders.csproj') -c Release -r win-x64 --self-contained -o $staging -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $smokeLog = Join-Path $staging 'smoke-errors.txt'
    $smoke = Start-Process -FilePath (Join-Path $staging 'DesktopFolders.exe') -ArgumentList '--smoke-ui' -RedirectStandardError $smokeLog -PassThru
    if (-not $smoke.WaitForExit(20000)) {
        $smoke.Kill()
        throw 'Windows UI smoke test timed out.'
    }
    if ($smoke.ExitCode -ne 0) {
        Get-Content -LiteralPath $smokeLog
        throw "Windows UI smoke test failed with exit code $($smoke.ExitCode)."
    }
    Remove-Item -LiteralPath $smokeLog
    $portable = Join-Path $artifacts 'DesktopFolders-win-x64'
    if (Test-Path -LiteralPath $portable) {
        $resolved = (Resolve-Path -LiteralPath $portable).ProviderPath
        $expected = [IO.Path]::Combine((Resolve-Path -LiteralPath $artifacts).ProviderPath, 'DesktopFolders-win-x64')
        if ($resolved -ne $expected -or (Get-Item -LiteralPath $resolved).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
            throw "Unexpected publish destination: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    Move-Item -LiteralPath $staging -Destination $portable
    $archive = Join-Path $artifacts 'DesktopFolders-win-x64.zip'
    Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $archive -Force
    Get-FileHash -LiteralPath $archive
    if ($Installer) {
        if (-not $Iscc) {
            $Iscc = @(
                (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
                "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
                "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
            ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
        }
        if (-not $Iscc) { throw 'Install Inno Setup 6 or pass the path to ISCC.exe with -Iscc.' }
        & $Iscc /Qp "/O$artifacts" "/DPublishDir=$portable" (Join-Path $root 'installer/DesktopFolders.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
        Get-FileHash -LiteralPath (Join-Path $artifacts 'DesktopFolders-Setup-win-x64.exe')
    }
}
finally {
    if (Test-Path -LiteralPath $staging) {
        $resolved = (Resolve-Path -LiteralPath $staging).ProviderPath
        $parent = (Resolve-Path -LiteralPath $artifacts).ProviderPath
        if ([IO.Path]::GetDirectoryName($resolved) -eq $parent -and [IO.Path]::GetFileName($resolved) -match '^publish-[0-9a-f]{32}$') {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
