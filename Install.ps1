# Instala Sync Two Files para el usuario actual (no requiere administrador).
# No modifica la configuracion existente (%APPDATA%\SyncTwoFiles\config.json).
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\SyncTwoFiles'),
    [switch]$NoDesktopShortcut,
    [switch]$NoLaunch
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

Get-Process powershell -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowTitle -eq 'Sync Two Files' } |
    Stop-Process -Force
Start-Sleep -Milliseconds 500

# Detener las sincronizaciones automaticas para que el agente las relance con la version nueva.
Import-Module "$here\src\SyncTwoFiles.Core.psm1" -Force -DisableNameChecking
try {
    $config = Read-SyncConfig (Get-AppPaths).ConfigFile
    foreach ($p in @($config.pairs)) { Stop-PairAutoSync $p }
    Start-Sleep -Seconds 1
} catch {
    Write-Warning $_.Exception.Message
}

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item "$here\src\SyncTwoFiles.ps1", "$here\src\SyncTwoFiles.Core.psm1" -Destination $InstallDir -Force
Copy-Item "$here\assets\SyncTwoFiles.ico", "$here\assets\SyncTwoFiles.png" -Destination $InstallDir -Force
Copy-Item "$here\Uninstall.ps1" -Destination $InstallDir -Force

$app = Join-Path $InstallDir 'SyncTwoFiles.ps1'
function New-Launcher([string]$Path, [string]$ExtraArgs) {
    $cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$app`" $ExtraArgs".Trim()
    $vbs = 'CreateObject("WScript.Shell").Run "' + ($cmd -replace '"', '""') + '", 0, False'
    Set-Content -Path $Path -Value $vbs -Encoding Unicode
}
$guiVbs = Join-Path $InstallDir 'Sync Two Files.vbs'
$agentVbs = Join-Path $InstallDir 'SyncTwoFiles-Agent.vbs'
New-Launcher $guiVbs ''
New-Launcher $agentVbs '-Agent'

$ws = New-Object -ComObject WScript.Shell
function New-Shortcut([string]$Path) {
    $lnk = $ws.CreateShortcut($Path)
    $lnk.TargetPath = Join-Path $env:SystemRoot 'System32\wscript.exe'
    $lnk.Arguments = "`"$guiVbs`""
    $lnk.WorkingDirectory = $InstallDir
    $lnk.IconLocation = (Join-Path $InstallDir 'SyncTwoFiles.ico') + ',0'
    $lnk.Description = 'Sync Two Files - respaldo automatico de carpetas'
    $lnk.Save()
}
New-Shortcut (Join-Path ([Environment]::GetFolderPath('Programs')) 'Sync Two Files.lnk')
if (-not $NoDesktopShortcut) { New-Shortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Sync Two Files.lnk') }

Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'SyncTwoFiles' -Value "wscript.exe `"$agentVbs`""

Start-Process wscript.exe -ArgumentList "`"$agentVbs`""
if (-not $NoLaunch) { Start-Process wscript.exe -ArgumentList "`"$guiVbs`"" }

"Sync Two Files instalado en $InstallDir"
