# Builds the NitroCustom installer: self-contained publish of App, Service and CLI into
# publish\dist, then compiles installer\NitroCustom.iss with Inno Setup 6.
#   powershell -ExecutionPolicy Bypass -File tools\build_installer.ps1 [-Version 0.1.0]
param([string]$Version = '0.1.0')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'publish\dist'

$iscc = @(
    (Get-Command iscc -ErrorAction SilentlyContinue).Source,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found (winget install JRSoftware.InnoSetup).' }

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
foreach ($p in 'NitroCustom.Agent', 'NitroCustom.App', 'NitroCustom.Service', 'NitroCustom.Cli') {
    dotnet publish (Join-Path $root "src\$p") -c Release -r win-x64 --self-contained true `
        -p:Version=$Version -o $dist --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $p" }
}
Get-ChildItem $dist -Filter *.pdb | Remove-Item -Force

& $iscc "/DAppVersion=$Version" "/DSourceDir=$dist" (Join-Path $root 'installer\NitroCustom.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compile failed' }
Get-ChildItem (Join-Path $root 'installer\Output') -Filter *.exe | Format-Table Name, Length -AutoSize
