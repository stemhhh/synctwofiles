# Desinstala Sync Two Files 2.0. Tus carpetas, backups y la papelera de versiones NO se tocan.
# Usa -RemoveData para borrar tambien la configuracion, el historial y los registros de la 2.0.
# La configuracion de la 1.x (%APPDATA%\SyncTwoFiles) nunca se borra desde aqui.
param(
    [string]$InstallDir = $PSScriptRoot,
    [switch]$RemoveData
)
$ErrorActionPreference = 'Continue'

$dataDir = Join-Path $env:LOCALAPPDATA 'SyncTwoFiles\v2'
$exe = Join-Path $InstallDir 'SyncTwoFiles.exe'

Get-Process SyncTwoFiles -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $exe } |
    Stop-Process -Force

# Solo las copias automaticas lanzadas por la 2.0: escriben su registro en v2\logs.
$logDir = Join-Path $dataDir 'logs'
Get-CimInstance Win32_Process -Filter "Name = 'robocopy.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($logDir, [StringComparison]::OrdinalIgnoreCase) -ge 0 } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'SyncTwoFiles.v2' -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SyncTwoFiles2' -Recurse -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'Sync Two Files 2.lnk') -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Sync Two Files 2.lnk') -ErrorAction SilentlyContinue

if ($RemoveData) { Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue }

Set-Location $env:TEMP
if (Test-Path (Join-Path $InstallDir 'SyncTwoFiles.exe')) {
    Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
}
"Sync Two Files 2 desinstalado."
