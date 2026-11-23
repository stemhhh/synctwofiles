# Desinstala Sync Two Files. Tus carpetas y backups NO se tocan.
# Usa -RemoveConfig para borrar tambien la configuracion y el historial.
param([switch]$RemoveConfig)
$ErrorActionPreference = 'Continue'

$InstallDir = $PSScriptRoot
Import-Module (Join-Path $InstallDir 'SyncTwoFiles.Core.psm1') -Force -DisableNameChecking
$paths = Get-AppPaths

Get-Process powershell -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowTitle -eq 'Sync Two Files' } |
    Stop-Process -Force

try {
    $config = Read-SyncConfig $paths.ConfigFile
    foreach ($p in @($config.pairs)) { Stop-PairAutoSync $p }
} catch { }

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'SyncTwoFiles' -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'Sync Two Files.lnk') -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Sync Two Files.lnk') -ErrorAction SilentlyContinue

if ($RemoveConfig) {
    Remove-Item $paths.ConfigDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Split-Path $paths.LogDir -Parent) -Recurse -Force -ErrorAction SilentlyContinue
}

Set-Location $env:TEMP
Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
"Sync Two Files desinstalado."
