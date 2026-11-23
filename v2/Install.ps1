# Instala Sync Two Files 2.0 para el usuario actual, sin MSIX ni administrador.
# La primera vez la app importa la configuracion de la 1.x en modo solo lectura
# (%APPDATA%\SyncTwoFiles\config.json no se modifica nunca).
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\SyncTwoFiles2'),
    [switch]$SkipBuild,
    [switch]$NoDesktopShortcut,
    [switch]$NoLaunch
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$publish = Join-Path $here 'obj\publish'

if (-not $SkipBuild) {
    dotnet publish (Join-Path $here 'SyncTwoFiles.App\SyncTwoFiles.App.csproj') -c Release -r win-x64 --self-contained `
        -p:DebugType=none -o $publish --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallo ($LASTEXITCODE)" }
}
if (-not (Test-Path (Join-Path $publish 'SyncTwoFiles.exe'))) { throw "No se encontro $publish\SyncTwoFiles.exe" }

# Solo se cierra la ventana; las copias automaticas (robocopy) siguen activas y la app las retoma al abrirse.
$exe = Join-Path $InstallDir 'SyncTwoFiles.exe'
Get-Process SyncTwoFiles -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $exe } |
    Stop-Process -Force
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
robocopy $publish $InstallDir /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "No se pudieron copiar los archivos (robocopy $LASTEXITCODE)" }
Copy-Item (Join-Path $here 'Uninstall.ps1') -Destination $InstallDir -Force

$ws = New-Object -ComObject WScript.Shell
function New-Shortcut([string]$Path) {
    $lnk = $ws.CreateShortcut($Path)
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $InstallDir
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = 'Sync Two Files - respaldo automatico de carpetas'
    $lnk.Save()
}
New-Shortcut (Join-Path ([Environment]::GetFolderPath('Programs')) 'Sync Two Files 2.lnk')
if (-not $NoDesktopShortcut) { New-Shortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Sync Two Files 2.lnk') }

$version = (Get-Item $exe).VersionInfo.ProductVersion -replace '\+.*$', ''
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SyncTwoFiles2'
New-Item -Path $uninstallKey -Force | Out-Null
$uninstallCmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $InstallDir 'Uninstall.ps1')`""
@{
    DisplayName     = 'Sync Two Files 2'
    DisplayVersion  = $version
    Publisher       = 'STEM'
    DisplayIcon     = "$exe,0"
    InstallLocation = $InstallDir
    UninstallString = $uninstallCmd
}.GetEnumerator() | ForEach-Object { Set-ItemProperty -Path $uninstallKey -Name $_.Key -Value $_.Value }
Set-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -Type DWord

# El inicio con Windows lo registra la propia app segun sus ajustes.
if (-not $NoLaunch) { Start-Process $exe }

"Sync Two Files $version instalado en $InstallDir"
exit 0
