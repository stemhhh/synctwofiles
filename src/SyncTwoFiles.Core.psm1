# Sync Two Files - logica de sincronizacion y configuracion (sin interfaz)

$script:AppInfo = [pscustomobject]@{
    Name    = 'Sync Two Files'
    Version = '1.1.1'
    Author  = 'Enmanuel'
    Id      = 'SyncTwoFiles'
}

function Get-AppInfo { $script:AppInfo }

function Get-AppPaths {
    $configDir = Join-Path $env:APPDATA 'SyncTwoFiles'
    $dataDir = Join-Path $env:LOCALAPPDATA 'SyncTwoFiles'
    [pscustomobject]@{
        ConfigDir  = $configDir
        ConfigFile = Join-Path $configDir 'config.json'
        LogDir     = Join-Path $dataDir 'logs'
    }
}

# ---------------------------------------------------------------- robocopy

# Carpetas de sistema que existen en la raiz de cada disco y no se deben copiar ni purgar.
$SystemDirs = @('/XD', '$RECYCLE.BIN', 'System Volume Information')
$CommonFlags = @('/COPY:DAT', '/DCOPY:T', '/XJ', '/R:2', '/W:5') + $SystemDirs

$MaxLogBytes = 5MB

function Get-AutoSyncArgs([string]$Source, [string]$Backup) {
    , (@($Source, $Backup, '/E') + $CommonFlags)
}

function Get-MonitorArgs([string]$Source, [string]$Backup, [string]$LogFile) {
    , ((Get-AutoSyncArgs $Source $Backup) + @('/NP', '/NFL', '/NDL', '/MON:1', '/MOT:1', "/UNILOG+:$LogFile"))
}

function Get-MirrorPreviewArgs([string]$Source, [string]$Backup) {
    , (@($Source, $Backup, '/MIR', '/XJ', '/L', '/NJS', '/FP') + $SystemDirs)
}

function Get-MirrorArgs([string]$Source, [string]$Backup) {
    , (@($Source, $Backup, '/MIR') + $CommonFlags)
}

function Get-RestoreArgs([string]$Source, [string]$Backup) {
    , (@($Backup, $Source, '/E', '/XO') + $CommonFlags)
}

function Test-RobocopySuccess([int]$ExitCode) {
    $ExitCode -ge 0 -and $ExitCode -lt 8
}

function Get-MirrorExtras([string[]]$LogLines) {
    $LogLines | Where-Object { $_ -match '\*EXTRA' } |
        ForEach-Object { ($_ -split "`t")[-1].Trim() } |
        Where-Object { $_ }
}

function Get-MirrorConfirm([string[]]$Extras, [int]$MaxShown = 12) {
    $Extras = @($Extras | Where-Object { $_ })
    if ($Extras.Count -eq 0) {
        return @{
            Title       = "¿Hacer espejo completo?"
            Text        = "No se borrará nada del backup. Solo se copiarán los cambios."
            OkText      = "Sincronizar"
            Destructive = $false
        }
    }
    $lista = ($Extras | Select-Object -First $MaxShown | ForEach-Object { Get-PathLabel $_ }) -join "`n"
    if ($Extras.Count -gt $MaxShown) { $lista += "`n… y $($Extras.Count - $MaxShown) más" }
    @{
        Title       = "Se borrarán $($Extras.Count) elemento(s) del backup"
        Text        = "Ya no existen en la carpeta de origen:`n`n$lista"
        OkText      = "Borrar y sincronizar"
        Destructive = $true
    }
}

# Una barra final antes de la comilla de cierre ("E:\") la escaparia; se duplica ("E:\\").
function ConvertTo-ArgumentString([string[]]$RoboArgs) {
    ($RoboArgs | ForEach-Object {
        if ($_ -match '\s') {
            $v = if ($_.EndsWith('\')) { "$_\" } else { $_ }
            "`"$v`""
        } else { $_ }
    }) -join ' '
}

# ---------------------------------------------------------------- rutas

# "E:" significa "carpeta actual del disco E", no la raiz: la raiz siempre conserva su barra.
function Get-NormalizedPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return '' }
    $p = $Path.Trim()
    if ($p -match '^[A-Za-z]:$') { return "$p\" }
    try { $p = [IO.Path]::GetFullPath($p) } catch { }
    $root = try { [IO.Path]::GetPathRoot($p) } catch { $null }
    if ($root -and $p.TrimEnd('\') -ieq $root.TrimEnd('\')) {
        if ($root -match '^[A-Za-z]:$') { $root += '\' }
        return $root
    }
    $p.TrimEnd('\')
}

function Test-IsDriveRoot([string]$Path) {
    $p = Get-NormalizedPath $Path
    $root = try { [IO.Path]::GetPathRoot($p) } catch { $null }
    [bool]$root -and $p -ieq $root
}

function Get-PathLabel([string]$Path) {
    $p = $Path.TrimEnd('\')
    $leaf = Split-Path $p -Leaf
    if ([string]::IsNullOrEmpty($leaf) -or $leaf -eq $p) { return Get-NormalizedPath $Path }
    $leaf
}

function Test-PathInside([string]$Child, [string]$Parent) {
    $c = (Get-NormalizedPath $Child).TrimEnd('\') + '\'
    $p = (Get-NormalizedPath $Parent).TrimEnd('\') + '\'
    $c.StartsWith($p, [StringComparison]::OrdinalIgnoreCase)
}

function Test-PathOverlap([string]$A, [string]$B) {
    (Test-PathInside $A $B) -or (Test-PathInside $B $A)
}

function Test-PairInput {
    param(
        [string]$Name,
        [string]$Source,
        [string]$Backup,
        [object[]]$Pairs = @(),
        [string]$ExcludeId
    )
    if ([string]::IsNullOrWhiteSpace($Name)) { return "Ponle un nombre a esta sincronización." }
    if ([string]::IsNullOrWhiteSpace($Source)) { return "Elige la carpeta que quieres respaldar." }
    if ([string]::IsNullOrWhiteSpace($Backup)) { return "Elige dónde guardar el backup." }
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { return "La carpeta a respaldar no existe." }

    $s = Get-NormalizedPath $Source
    $b = Get-NormalizedPath $Backup
    if (Test-IsDriveRoot $b) {
        return "El backup no puede ser un disco entero ($b). Elige o crea una carpeta dentro del disco, por ejemplo $($b)Backup."
    }
    if ($s -ieq $b) { return "La carpeta a respaldar y la del backup no pueden ser la misma." }
    if (Test-PathInside $b $s) { return "El backup no puede estar dentro de la carpeta que respaldas." }
    if (Test-PathInside $s $b) { return "La carpeta a respaldar no puede estar dentro del backup." }

    foreach ($p in @($Pairs | Where-Object { $_ -and $_.id -ne $ExcludeId })) {
        if ([string]$p.name -and $p.name.Trim() -ieq $Name.Trim()) {
            return "Ya existe una sincronización llamada «$($p.name)»."
        }
        if ((Get-NormalizedPath $p.source) -ieq $s -and (Get-NormalizedPath $p.backup) -ieq $b) {
            return "Ya existe una sincronización con estas mismas carpetas («$($p.name)»)."
        }
        if (Test-PathOverlap $b $p.backup) {
            return "Ese backup se mezcla con el de «$($p.name)». Usa una carpeta de backup distinta para cada sincronización."
        }
    }
    $null
}

# ---------------------------------------------------------------- configuracion

function New-SyncConfig {
    [pscustomobject]@{ version = 1; pairs = @() }
}

function New-SyncPair {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Backup,
        [bool]$Locked = $true,
        [bool]$AutoSync = $true
    )
    [pscustomobject]@{
        id        = [guid]::NewGuid().ToString()
        name      = $Name.Trim()
        source    = Get-NormalizedPath $Source
        backup    = Get-NormalizedPath $Backup
        locked    = $Locked
        autoSync  = $AutoSync
        createdAt = (Get-Date).ToString('s')
    }
}

function ConvertTo-SyncPair($Raw) {
    [pscustomobject]@{
        id        = if ($Raw.id) { [string]$Raw.id } else { [guid]::NewGuid().ToString() }
        name      = [string]$Raw.name
        source    = Get-NormalizedPath ([string]$Raw.source)
        backup    = Get-NormalizedPath ([string]$Raw.backup)
        locked    = [bool]$Raw.locked
        autoSync  = [bool]$Raw.autoSync
        createdAt = [string]$Raw.createdAt
    }
}

function Read-SyncConfig([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return New-SyncConfig }
    try {
        $obj = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
    } catch {
        $copy = "$Path.dañado-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Copy-Item -LiteralPath $Path -Destination $copy -Force
        throw "El archivo de configuración está dañado y no se pudo leer. Se guardó una copia en: $copy"
    }
    $pairs = @($obj.pairs | Where-Object { $_ } | ForEach-Object { ConvertTo-SyncPair $_ })
    [pscustomobject]@{ version = 1; pairs = $pairs }
}

function Save-SyncConfig($Config, [string]$Path) {
    $dir = Split-Path $Path -Parent
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $json = [pscustomobject]@{ version = 1; pairs = @($Config.pairs) } | ConvertTo-Json -Depth 5
    $tmp = "$Path.tmp"
    [IO.File]::WriteAllText($tmp, $json, (New-Object Text.UTF8Encoding $false))
    if (Test-Path -LiteralPath $Path) { Copy-Item -LiteralPath $Path -Destination "$Path.bak" -Force }
    Move-Item -LiteralPath $tmp -Destination $Path -Force
}

function Get-SyncPair($Config, [string]$Id) {
    if (-not $Id) { return $null }
    @($Config.pairs) | Where-Object { $_.id -eq $Id } | Select-Object -First 1
}

function Add-SyncPair($Config, $Pair) {
    $Config.pairs = @(@($Config.pairs) + $Pair)
}

function Assert-Unlocked($Pair) {
    if (-not $Pair) { throw "No se encontró la sincronización." }
    if ($Pair.locked) { throw "La configuración «$($Pair.name)» está bloqueada. Desbloquéala primero." }
}

function Update-SyncPair {
    param($Config, [string]$Id, [string]$Name, [string]$Source, [string]$Backup)
    $p = Get-SyncPair $Config $Id
    Assert-Unlocked $p
    $p.name = $Name.Trim()
    $p.source = Get-NormalizedPath $Source
    $p.backup = Get-NormalizedPath $Backup
}

function Remove-SyncPair($Config, [string]$Id) {
    Assert-Unlocked (Get-SyncPair $Config $Id)
    $Config.pairs = @(@($Config.pairs) | Where-Object { $_.id -ne $Id })
}

function Lock-SyncPair($Config, [string]$Id) {
    $p = Get-SyncPair $Config $Id
    if ($p) { $p.locked = $true }
}

function Unlock-SyncPair($Config, [string]$Id) {
    $p = Get-SyncPair $Config $Id
    if ($p) { $p.locked = $false }
}

function Test-UnlockConfirmation([string]$Expected, [string]$Typed) {
    if ([string]::IsNullOrEmpty($Expected) -or $null -eq $Typed) { return $false }
    $Typed.Trim() -ceq $Expected.Trim()
}

# ---------------------------------------------------------------- sincronizacion automatica

function Get-PairLogPath($Pair, [string]$LogDir) {
    Join-Path $LogDir "$($Pair.id).log"
}

function Test-IsUtf16File([string]$Path) {
    try {
        $fs = [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
        try {
            $b = New-Object byte[] 4
            $n = $fs.Read($b, 0, 4)
        } finally { $fs.Close() }
    } catch { return $true }
    if ($n -lt 2) { return $true }
    if ($b[0] -eq 0xFF -and $b[1] -eq 0xFE) { return $true }
    $n -ge 4 -and $b[1] -eq 0 -and $b[3] -eq 0
}

# robocopy /UNILOG+ escribe UTF-16 sin BOM y anade al final: un log viejo en otro formato o
# demasiado grande se archiva, y uno nuevo se crea con BOM para que el Bloc de notas lo lea bien.
function Reset-PairLogIfNeeded([string]$LogFile) {
    if (Test-Path -LiteralPath $LogFile) {
        $item = Get-Item -LiteralPath $LogFile
        if ((Test-IsUtf16File $LogFile) -and $item.Length -le $MaxLogBytes) { return }
        $old = [IO.Path]::ChangeExtension($LogFile, '.old.log')
        try { Move-Item -LiteralPath $LogFile -Destination $old -Force -ErrorAction Stop } catch { return }
    }
    try { [IO.File]::WriteAllBytes($LogFile, [byte[]](0xFF, 0xFE)) } catch { }
}

function Test-IsAutoSyncCommandLine([string]$CommandLine, [string]$Source, [string]$Backup) {
    if (-not $CommandLine -or $CommandLine -notmatch '/MON:') { return $false }
    $s = [regex]::Escape((Get-NormalizedPath $Source).TrimEnd('\'))
    $b = [regex]::Escape((Get-NormalizedPath $Backup).TrimEnd('\'))
    $CommandLine -match "(^|\s)`"?$s\\*`"?\s+`"?$b\\*`"?(\s|$)"
}

function Get-RobocopyProcesses {
    if (-not (Get-Process robocopy -ErrorAction SilentlyContinue)) { return @() }
    @(Get-CimInstance Win32_Process -Filter "Name='Robocopy.exe'")
}

function Get-PairAutoSyncProcess($Pair, [object[]]$Processes) {
    if ($null -eq $Processes) { $Processes = Get-RobocopyProcesses }
    @($Processes | Where-Object { Test-IsAutoSyncCommandLine $_.CommandLine $Pair.source $Pair.backup })
}

function Get-PairAvailability($Pair) {
    if (-not (Test-Path -LiteralPath $Pair.source -PathType Container)) { return 'SourceMissing' }
    $root = try { [IO.Path]::GetPathRoot($Pair.backup) } catch { $null }
    if (-not $root -or -not (Test-Path -LiteralPath $root)) { return 'BackupMissing' }
    'Ok'
}

function Start-PairAutoSync($Pair, [string]$LogDir) {
    if ((Get-PairAvailability $Pair) -ne 'Ok') { return $false }
    try {
        New-Item -ItemType Directory -Path $LogDir -Force -ErrorAction Stop | Out-Null
        New-Item -ItemType Directory -Path $Pair.backup -Force -ErrorAction Stop | Out-Null
        $log = Get-PairLogPath $Pair $LogDir
        Reset-PairLogIfNeeded $log
        $argString = ConvertTo-ArgumentString (Get-MonitorArgs $Pair.source $Pair.backup $log)
        Start-Process robocopy -ArgumentList $argString -WindowStyle Hidden -ErrorAction Stop | Out-Null
        $true
    } catch { $false }
}

function Stop-PairAutoSync($Pair) {
    Get-PairAutoSyncProcess $Pair | ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

function Start-AllAutoSync($Config, [string]$LogDir, [object[]]$Processes) {
    if ($null -eq $Processes) { $Processes = @(Get-RobocopyProcesses) }
    $started = 0
    foreach ($p in @($Config.pairs | Where-Object { $_.autoSync })) {
        try {
            if (@(Get-PairAutoSyncProcess $p $Processes).Count -eq 0 -and (Start-PairAutoSync $p $LogDir)) { $started++ }
        } catch { }
    }
    $started
}

Export-ModuleMember -Function *
