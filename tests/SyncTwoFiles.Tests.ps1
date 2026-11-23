BeforeAll {
    $script:Root = Split-Path $PSScriptRoot -Parent
    Import-Module (Join-Path $Root "src\SyncTwoFiles.Core.psm1") -Force -DisableNameChecking

    function Invoke-Robo([string[]]$RoboArgs) {
        $output = & robocopy @RoboArgs /NP /NJH 2>&1
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = @($output) }
    }

    function New-TestFile([string]$Path, [string]$Content = "x") {
        New-Item -ItemType Directory -Path (Split-Path $Path) -Force | Out-Null
        Set-Content -Path $Path -Value $Content -Encoding UTF8
    }

    function New-Pair {
        $base = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $src = Join-Path $base "Escritorio con espacios ñ"
        $dst = Join-Path $base "Almacen 1\Backup"
        New-Item -ItemType Directory -Path $src, $dst -Force | Out-Null
        [pscustomobject]@{ Src = $src; Dst = $dst }
    }

    function Wait-Until([scriptblock]$Condition, [int]$TimeoutSec = 20) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
            if (& $Condition) { return $true }
            Start-Sleep -Milliseconds 250
        }
        $false
    }
}

Describe "Información de la app" {
    It "tiene nombre y versión" {
        $i = Get-AppInfo
        $i.Name | Should -Be "Sync Two Files"
        $i.Version | Should -Match '^\d+\.\d+\.\d+$'
    }

    It "guarda la configuración en el perfil del usuario, no en la carpeta del programa" {
        $p = Get-AppPaths
        $p.ConfigFile | Should -BeLike "$env:APPDATA\*"
        $p.LogDir | Should -BeLike "$env:LOCALAPPDATA\*"
    }
}

Describe "Argumentos de robocopy" {
    It "la sincronización automática copia subcarpetas y nunca borra" {
        $a = Get-AutoSyncArgs "C:\A" "C:\B"
        $a[0] | Should -Be "C:\A"
        $a[1] | Should -Be "C:\B"
        $a | Should -Contain "/E"
        $a | Should -Not -Contain "/MIR"
        $a | Should -Not -Contain "/PURGE"
        $a | Should -Not -Contain "/MOV"
    }

    It "el modo vigilancia añade /MON y el log, sin opciones que borren" {
        $a = Get-MonitorArgs "C:\A" "C:\B" "C:\logs\x.log"
        $a | Should -Contain "/MON:1"
        $a | Should -Contain "/UNILOG+:C:\logs\x.log"
        $a | Should -Not -Contain "/MIR"
        $a | Should -Not -Contain "/PURGE"
    }

    It "el espejo completo usa /MIR de origen hacia backup" {
        $a = Get-MirrorArgs "C:\A" "C:\B"
        $a[0] | Should -Be "C:\A"
        $a[1] | Should -Be "C:\B"
        $a | Should -Contain "/MIR"
    }

    It "la vista previa del espejo es solo de lectura (/L)" {
        $a = Get-MirrorPreviewArgs "C:\A" "C:\B"
        $a | Should -Contain "/L"
        $a | Should -Contain "/MIR"
    }

    It "restaurar va de backup hacia origen, sin borrar ni pisar archivos más nuevos" {
        $a = Get-RestoreArgs "C:\A" "C:\B"
        $a[0] | Should -Be "C:\B"
        $a[1] | Should -Be "C:\A"
        $a | Should -Contain "/XO"
        $a | Should -Not -Contain "/MIR"
    }

    It "pone comillas solo a los argumentos con espacios" {
        ConvertTo-ArgumentString @("C:\Mi Carpeta", "C:\B", "/E") | Should -Be '"C:\Mi Carpeta" C:\B /E'
    }

    It "duplica la barra final dentro de comillas para que no escape la comilla" {
        ConvertTo-ArgumentString @("C:\Mi Carpeta\", "E:\") | Should -Be '"C:\Mi Carpeta\\" E:\'
    }

    It "todas las operaciones excluyen las carpetas de sistema de los discos" -ForEach @(
        @{ N = 'auto'; A = { Get-AutoSyncArgs "C:\A" "D:\B" } },
        @{ N = 'espejo'; A = { Get-MirrorArgs "C:\A" "D:\B" } },
        @{ N = 'vista previa'; A = { Get-MirrorPreviewArgs "C:\A" "D:\B" } },
        @{ N = 'restaurar'; A = { Get-RestoreArgs "C:\A" "D:\B" } }
    ) {
        $a = & $A
        $a | Should -Contain '/XD'
        $a | Should -Contain '$RECYCLE.BIN'
        $a | Should -Contain 'System Volume Information'
    }
}

Describe "Rutas" {
    It "la raíz de un disco conserva su barra (C: sin barra es otra carpeta)" {
        Get-NormalizedPath "C:" | Should -Be "C:\"
        Get-NormalizedPath "C:\" | Should -Be "C:\"
        Get-NormalizedPath "e:\\" | Should -Be "e:\"
    }

    It "quita la barra final de las carpetas normales" {
        Get-NormalizedPath "C:\Fotos\" | Should -Be "C:\Fotos"
        Get-NormalizedPath "  C:\Fotos  " | Should -Be "C:\Fotos"
    }

    It "reconoce la raíz de un disco" {
        Test-IsDriveRoot "E:\" | Should -BeTrue
        Test-IsDriveRoot "E:\Backup" | Should -BeFalse
    }

    It "muestra un nombre corto legible" {
        Get-PathLabel "C:\Users\x\Fotos" | Should -Be "Fotos"
        Get-PathLabel "E:\" | Should -Be "E:\"
    }

    It "una carpeta en la raíz está dentro del disco" {
        Test-PathInside "C:\Fotos" "C:\" | Should -BeTrue
        Test-PathInside "D:\Fotos" "C:\" | Should -BeFalse
    }

    It "un robocopy real con raíz o barra final entre comillas recibe bien las rutas" {
        $src = Join-Path $TestDrive "origen con espacios"
        $dst = Join-Path $TestDrive "destino con espacios"
        New-TestFile "$src\a.txt"
        $out = Join-Path $TestDrive "out.txt"
        Start-Process robocopy -ArgumentList (ConvertTo-ArgumentString @("$src\", $dst, "/E")) -NoNewWindow -Wait -RedirectStandardOutput $out
        "$dst\a.txt" | Should -Exist
    }
}

Describe "Códigos de salida" {
    It "el código <_> es éxito" -ForEach 0, 1, 2, 3, 4, 5, 6, 7 {
        Test-RobocopySuccess $_ | Should -BeTrue
    }
    It "el código <_> es error" -ForEach 8, 16, -1 {
        Test-RobocopySuccess $_ | Should -BeFalse
    }
}

Describe "Vista previa y confirmación del espejo" {
    It "extrae las rutas marcadas como EXTRA" {
        $lines = @(
            "`t    New File  `t`t      10`tC:\src\nuevo.txt",
            "`t*EXTRA File `t`t      20`tC:\dst\viejo.txt",
            "`t*EXTRA Dir        -1`tC:\dst\carpeta vieja\"
        )
        $e = @(Get-MirrorExtras $lines)
        $e.Count | Should -Be 2
        $e | Should -Contain "C:\dst\viejo.txt"
    }

    It "devuelve una lista vacía si no hay nada que borrar" {
        @(Get-MirrorExtras @("nada", "")).Count | Should -Be 0
    }

    It "la confirmación solo es destructiva si hay algo que borrar" {
        (Get-MirrorConfirm @()).Destructive | Should -BeFalse
        $c = Get-MirrorConfirm @("C:\dst\a.txt")
        $c.Destructive | Should -BeTrue
        $c.Text | Should -Match "a\.txt"
    }

    It "recorta listas largas" {
        $c = Get-MirrorConfirm (1..20 | ForEach-Object { "C:\dst\f$_.txt" }) -MaxShown 12
        $c.Text | Should -Match "y 8 más"
    }
}

Describe "Validación de carpetas" {
    BeforeAll {
        $script:P = New-Pair
    }

    It "acepta una configuración válida" {
        Test-PairInput -Name "Fotos" -Source $P.Src -Backup $P.Dst | Should -BeNullOrEmpty
    }

    It "exige un nombre" {
        Test-PairInput -Name " " -Source $P.Src -Backup $P.Dst | Should -Match "nombre"
    }

    It "exige que la carpeta a respaldar exista" {
        Test-PairInput -Name "X" -Source "$TestDrive\no-existe" -Backup $P.Dst | Should -Match "no existe"
    }

    It "no permite la misma carpeta en ambos lados" {
        Test-PairInput -Name "X" -Source $P.Src -Backup "$($P.Src)\" | Should -Match "misma"
    }

    It "no permite el backup dentro de la carpeta respaldada" {
        Test-PairInput -Name "X" -Source $P.Src -Backup "$($P.Src)\backup" | Should -Match "dentro"
    }

    It "no permite la carpeta respaldada dentro del backup" {
        $inner = Join-Path $P.Dst "sub"
        New-Item -ItemType Directory $inner -Force | Out-Null
        Test-PairInput -Name "X" -Source $inner -Backup $P.Dst | Should -Match "dentro"
    }

    It "no confunde carpetas con nombres parecidos (C:\A y C:\AB)" {
        $a = Join-Path $TestDrive "Proy"
        New-Item -ItemType Directory $a -Force | Out-Null
        Test-PairInput -Name "X" -Source $a -Backup (Join-Path $TestDrive "ProyBackup") | Should -BeNullOrEmpty
    }

    It "no permite duplicar una sincronización existente" {
        $existing = New-SyncPair -Name "Uno" -Source $P.Src -Backup $P.Dst
        Test-PairInput -Name "Dos" -Source $P.Src -Backup $P.Dst -Pairs @($existing) | Should -Match "mismas carpetas"
    }

    It "no permite dos sincronizaciones con el mismo nombre" {
        $existing = New-SyncPair -Name "Fotos" -Source $P.Src -Backup "$TestDrive\otro"
        Test-PairInput -Name "fotos" -Source $P.Src -Backup $P.Dst -Pairs @($existing) | Should -Match "llamada"
    }

    It "no permite que el backup sea un disco entero" {
        $drive = [IO.Path]::GetPathRoot($TestDrive)
        Test-PairInput -Name "X" -Source $P.Src -Backup $drive | Should -Match "disco entero"
    }

    It "no permite que dos sincronizaciones compartan la misma carpeta de backup" {
        $other = New-Pair
        $existing = New-SyncPair -Name "Uno" -Source $other.Src -Backup $P.Dst
        Test-PairInput -Name "Dos" -Source $P.Src -Backup $P.Dst -Pairs @($existing) | Should -Match "se mezcla"
    }

    It "no permite un backup dentro del backup de otra sincronización" {
        $other = New-Pair
        $existing = New-SyncPair -Name "Uno" -Source $other.Src -Backup $P.Dst
        Test-PairInput -Name "Dos" -Source $P.Src -Backup "$($P.Dst)\sub" -Pairs @($existing) | Should -Match "dentro|se mezcla"
    }

    It "al editar no choca consigo misma" {
        $existing = New-SyncPair -Name "Uno" -Source $P.Src -Backup $P.Dst
        Test-PairInput -Name "Uno" -Source $P.Src -Backup $P.Dst -Pairs @($existing) -ExcludeId $existing.id | Should -BeNullOrEmpty
    }
}

Describe "Configuración (varias sincronizaciones)" {
    BeforeEach {
        $script:File = Join-Path $TestDrive "cfg-$([guid]::NewGuid().ToString('N'))\config.json"
    }

    It "sin archivo empieza vacía" {
        @((Read-SyncConfig $File).pairs).Count | Should -Be 0
    }

    It "guarda y lee una sola sincronización como lista" {
        $c = New-SyncConfig
        Add-SyncPair $c (New-SyncPair -Name "Uno" -Source "C:\A" -Backup "D:\B")
        Save-SyncConfig $c $File
        $r = Read-SyncConfig $File
        @($r.pairs).Count | Should -Be 1
        $r.pairs[0].name | Should -Be "Uno"
        $r.pairs[0].locked | Should -BeTrue
        (Get-Content $File -Raw) | Should -Match '"pairs":\s*\['
    }

    It "guarda y lee tres sincronizaciones en ubicaciones distintas" {
        $c = New-SyncConfig
        Add-SyncPair $c (New-SyncPair -Name "Proyectos" -Source "C:\Proyectos" -Backup "D:\Backup1")
        Add-SyncPair $c (New-SyncPair -Name "Fotos" -Source "C:\Fotos" -Backup "E:\Backup2" -Locked $false)
        Add-SyncPair $c (New-SyncPair -Name "Música ñ" -Source "C:\Música" -Backup "F:\Backup 3" -AutoSync $false)
        Save-SyncConfig $c $File
        $r = Read-SyncConfig $File
        @($r.pairs).Count | Should -Be 3
        ($r.pairs | Where-Object name -eq "Fotos").locked | Should -BeFalse
        ($r.pairs | Where-Object name -eq "Música ñ").autoSync | Should -BeFalse
        ($r.pairs | Where-Object name -eq "Música ñ").backup | Should -Be "F:\Backup 3"
    }

    It "guarda una lista vacía" {
        Save-SyncConfig (New-SyncConfig) $File
        @((Read-SyncConfig $File).pairs).Count | Should -Be 0
    }

    It "guarda una copia .bak de la versión anterior" {
        $c = New-SyncConfig
        Save-SyncConfig $c $File
        Add-SyncPair $c (New-SyncPair -Name "Uno" -Source "C:\A" -Backup "D:\B")
        Save-SyncConfig $c $File
        "$File.bak" | Should -Exist
    }

    It "si el archivo está dañado avisa, guarda una copia y no lo sobrescribe" {
        New-Item -ItemType Directory (Split-Path $File) -Force | Out-Null
        Set-Content $File "{ esto no es json" -Encoding UTF8
        { Read-SyncConfig $File } | Should -Throw "*dañado*"
        Get-Content $File -Raw | Should -Match "esto no es json"
        @(Get-ChildItem (Split-Path $File) -Filter "config.json.dañado-*").Count | Should -Be 1
    }
}

Describe "Bloqueo de configuración" {
    BeforeEach {
        $script:C = New-SyncConfig
        $script:Locked = New-SyncPair -Name "Protegida" -Source "C:\A" -Backup "D:\B" -Locked $true
        $script:Free = New-SyncPair -Name "Libre" -Source "C:\C" -Backup "D:\D" -Locked $false
        Add-SyncPair $C $Locked
        Add-SyncPair $C $Free
    }

    It "las nuevas sincronizaciones se bloquean por defecto" {
        (New-SyncPair -Name "X" -Source "C:\X" -Backup "D:\X").locked | Should -BeTrue
    }

    It "no se puede editar una configuración bloqueada" {
        { Update-SyncPair -Config $C -Id $Locked.id -Name "Otro" -Source "C:\Z" -Backup "D:\Z" } | Should -Throw "*bloqueada*"
        (Get-SyncPair $C $Locked.id).source | Should -Be "C:\A"
    }

    It "no se puede eliminar una configuración bloqueada" {
        { Remove-SyncPair $C $Locked.id } | Should -Throw "*bloqueada*"
        @($C.pairs).Count | Should -Be 2
    }

    It "una configuración libre sí se puede editar y eliminar" {
        Update-SyncPair -Config $C -Id $Free.id -Name "Libre 2" -Source "C:\C2" -Backup "D:\D2"
        (Get-SyncPair $C $Free.id).name | Should -Be "Libre 2"
        Remove-SyncPair $C $Free.id
        @($C.pairs).Count | Should -Be 1
    }

    It "después de desbloquearla se puede eliminar" {
        Unlock-SyncPair $C $Locked.id
        Remove-SyncPair $C $Locked.id
        Get-SyncPair $C $Locked.id | Should -BeNullOrEmpty
    }

    It "se puede volver a bloquear" {
        Lock-SyncPair $C $Free.id
        { Remove-SyncPair $C $Free.id } | Should -Throw
    }

    It "eliminar una no afecta a las demás" {
        Remove-SyncPair $C $Free.id
        (Get-SyncPair $C $Locked.id).name | Should -Be "Protegida"
    }

    It "para desbloquear hay que escribir el nombre exacto" {
        Test-UnlockConfirmation "Protegida" "Protegida" | Should -BeTrue
        Test-UnlockConfirmation "Protegida" "  Protegida " | Should -BeTrue
        Test-UnlockConfirmation "Protegida" "protegida" | Should -BeFalse
        Test-UnlockConfirmation "Protegida" "Proteg" | Should -BeFalse
        Test-UnlockConfirmation "Protegida" "" | Should -BeFalse
    }

    It "el bloqueo se conserva al guardar y leer" {
        $f = Join-Path $TestDrive "lock-$([guid]::NewGuid().ToString('N')).json"
        Save-SyncConfig $C $f
        $r = Read-SyncConfig $f
        (Get-SyncPair $r $Locked.id).locked | Should -BeTrue
        (Get-SyncPair $r $Free.id).locked | Should -BeFalse
    }
}

Describe "Reconocer procesos de sincronización automática" {
    It "reconoce la línea de comandos con comillas" {
        $cmd = 'robocopy  "C:\Users\enman\Desktop\PROYECTOS DIGITALES" "C:\Almacen 1\Backup Local de ProyectosDigitales" /E /COPY:DAT /MON:1 /MOT:1 /LOG+:"C:\x.log"'
        Test-IsAutoSyncCommandLine $cmd "C:\Users\enman\Desktop\PROYECTOS DIGITALES" "C:\Almacen 1\Backup Local de ProyectosDigitales" | Should -BeTrue
    }

    It "reconoce la línea de comandos sin comillas" {
        Test-IsAutoSyncCommandLine 'robocopy C:\A D:\B /E /MON:1' "C:\A" "D:\B" | Should -BeTrue
    }

    It "ignora copias que no son de vigilancia" {
        Test-IsAutoSyncCommandLine 'robocopy C:\A D:\B /MIR' "C:\A" "D:\B" | Should -BeFalse
    }

    It "no confunde carpetas con nombres parecidos" {
        Test-IsAutoSyncCommandLine 'robocopy C:\AB D:\B /E /MON:1' "C:\A" "D:\B" | Should -BeFalse
        Test-IsAutoSyncCommandLine 'robocopy C:\A D:\B2 /E /MON:1' "C:\A" "D:\B" | Should -BeFalse
    }

    It "distingue entre varias sincronizaciones" {
        $procs = @(
            [pscustomobject]@{ ProcessId = 1; CommandLine = 'robocopy C:\Fotos E:\B2 /E /MON:1' },
            [pscustomobject]@{ ProcessId = 2; CommandLine = 'robocopy "C:\Mis Proyectos" "D:\B 1" /E /MON:1' }
        )
        $pair = [pscustomobject]@{ source = "C:\Mis Proyectos"; backup = "D:\B 1" }
        $found = @(Get-PairAutoSyncProcess $pair $procs)
        $found.Count | Should -Be 1
        $found[0].ProcessId | Should -Be 2
    }
}

Describe "Sincronización real con robocopy" {
    It "copia archivos y subcarpetas nuevas" {
        $p = New-Pair
        New-TestFile "$($p.Src)\a.txt"
        New-TestFile "$($p.Src)\sub\b.txt"
        Test-RobocopySuccess (Invoke-Robo (Get-AutoSyncArgs $p.Src $p.Dst)).ExitCode | Should -BeTrue
        "$($p.Dst)\a.txt" | Should -Exist
        "$($p.Dst)\sub\b.txt" | Should -Exist
    }

    It "lo que se borra del origen se queda en el backup" {
        $p = New-Pair
        New-TestFile "$($p.Src)\borrame.txt"
        Invoke-Robo (Get-AutoSyncArgs $p.Src $p.Dst) | Out-Null
        Remove-Item "$($p.Src)\borrame.txt"
        Invoke-Robo (Get-AutoSyncArgs $p.Src $p.Dst) | Out-Null
        "$($p.Dst)\borrame.txt" | Should -Exist
    }

    It "actualiza en el backup los archivos modificados" {
        $p = New-Pair
        New-TestFile "$($p.Src)\doc.txt" "version 1"
        Invoke-Robo (Get-AutoSyncArgs $p.Src $p.Dst) | Out-Null
        Set-Content "$($p.Src)\doc.txt" "version 2" -Encoding UTF8
        (Get-Item "$($p.Src)\doc.txt").LastWriteTime = (Get-Date).AddMinutes(5)
        Invoke-Robo (Get-AutoSyncArgs $p.Src $p.Dst) | Out-Null
        Get-Content "$($p.Dst)\doc.txt" | Should -Be "version 2"
    }

    It "la vista previa detecta lo que sobra sin borrar nada" {
        $p = New-Pair
        New-TestFile "$($p.Src)\queda.txt"
        New-TestFile "$($p.Dst)\sobra.txt"
        $extras = @(Get-MirrorExtras (Invoke-Robo (Get-MirrorPreviewArgs $p.Src $p.Dst)).Output)
        ($extras | Where-Object { $_ -like "*sobra.txt" }) | Should -Not -BeNullOrEmpty
        "$($p.Dst)\sobra.txt" | Should -Exist
        "$($p.Dst)\queda.txt" | Should -Not -Exist
    }

    It "el espejo completo deja el backup idéntico y no toca el origen" {
        $p = New-Pair
        New-TestFile "$($p.Src)\queda.txt"
        New-TestFile "$($p.Dst)\sobra.txt"
        New-TestFile "$($p.Dst)\vieja\x.txt"
        Test-RobocopySuccess (Invoke-Robo (Get-MirrorArgs $p.Src $p.Dst)).ExitCode | Should -BeTrue
        "$($p.Dst)\queda.txt" | Should -Exist
        "$($p.Dst)\sobra.txt" | Should -Not -Exist
        "$($p.Dst)\vieja" | Should -Not -Exist
        "$($p.Src)\queda.txt" | Should -Exist
        "$($p.Src)\sobra.txt" | Should -Not -Exist
    }

    It "restaurar trae lo que falta sin borrar ni pisar archivos más nuevos" {
        $p = New-Pair
        New-TestFile "$($p.Dst)\perdido.txt"
        New-TestFile "$($p.Src)\solo-origen.txt"
        New-TestFile "$($p.Dst)\doc.txt" "viejo del backup"
        New-TestFile "$($p.Src)\doc.txt" "nuevo del origen"
        (Get-Item "$($p.Dst)\doc.txt").LastWriteTime = (Get-Date).AddDays(-1)
        Test-RobocopySuccess (Invoke-Robo (Get-RestoreArgs $p.Src $p.Dst)).ExitCode | Should -BeTrue
        "$($p.Src)\perdido.txt" | Should -Exist
        "$($p.Src)\solo-origen.txt" | Should -Exist
        Get-Content "$($p.Src)\doc.txt" | Should -Be "nuevo del origen"
    }
}

Describe "Sincronización automática real" {
    BeforeAll {
        $script:Started = @()
    }
    AfterAll {
        foreach ($pair in $script:Started) { Stop-PairAutoSync $pair }
    }

    It "arranca, copia, se reconoce y se detiene (con espacios en las rutas y en el log)" {
        $p = New-Pair
        New-TestFile "$($p.Src)\inicial.txt"
        $pair = New-SyncPair -Name "Prueba" -Source $p.Src -Backup $p.Dst
        $logDir = Join-Path $TestDrive "logs con espacio"
        $script:Started += $pair

        Start-PairAutoSync $pair $logDir | Should -BeTrue
        Wait-Until { Test-Path "$($p.Dst)\inicial.txt" } | Should -BeTrue
        Wait-Until { @(Get-PairAutoSyncProcess $pair).Count -eq 1 } | Should -BeTrue
        Get-PairLogPath $pair $logDir | Should -Exist

        Stop-PairAutoSync $pair
        Wait-Until { @(Get-PairAutoSyncProcess $pair).Count -eq 0 } | Should -BeTrue
    }

    It "arranca solo las sincronizaciones con automático activado" {
        $a = New-Pair; $b = New-Pair
        $on = New-SyncPair -Name "On" -Source $a.Src -Backup $a.Dst -AutoSync $true
        $off = New-SyncPair -Name "Off" -Source $b.Src -Backup $b.Dst -AutoSync $false
        $c = New-SyncConfig; Add-SyncPair $c $on; Add-SyncPair $c $off
        $script:Started += $on, $off

        Start-AllAutoSync $c (Join-Path $TestDrive "logs")
        Wait-Until { @(Get-PairAutoSyncProcess $on).Count -eq 1 } | Should -BeTrue
        @(Get-PairAutoSyncProcess $off).Count | Should -Be 0

        Start-AllAutoSync $c (Join-Path $TestDrive "logs")
        Start-Sleep -Milliseconds 500
        @(Get-PairAutoSyncProcess $on).Count | Should -Be 1 -Because "no debe arrancar dos veces la misma"
    }

    It "no arranca si la carpeta a respaldar no existe" {
        $pair = New-SyncPair -Name "X" -Source "$TestDrive\no-existe" -Backup "$TestDrive\b"
        Start-PairAutoSync $pair (Join-Path $TestDrive "logs") | Should -BeFalse
        Get-PairAvailability $pair | Should -Be 'SourceMissing'
    }

    It "no falla si el disco del backup no está conectado" {
        $p = New-Pair
        $used = (Get-PSDrive -PSProvider FileSystem).Name
        $free = [char[]](70..90) | Where-Object { $used -notcontains [string]$_ } | Select-Object -Last 1
        $pair = New-SyncPair -Name "X" -Source $p.Src -Backup "$($free):\Backup"
        Get-PairAvailability $pair | Should -Be 'BackupMissing'
        { Start-PairAutoSync $pair (Join-Path $TestDrive "logs") } | Should -Not -Throw
        Start-PairAutoSync $pair (Join-Path $TestDrive "logs") | Should -BeFalse
    }

    It "si una sincronización falla, las demás arrancan igual" {
        $ok = New-Pair
        $bad = New-SyncPair -Name "Mala" -Source "$TestDrive\no-existe" -Backup "$TestDrive\b2"
        $good = New-SyncPair -Name "Buena" -Source $ok.Src -Backup $ok.Dst
        $c = New-SyncConfig; Add-SyncPair $c $bad; Add-SyncPair $c $good
        $script:Started += $good
        Start-AllAutoSync $c (Join-Path $TestDrive "logs") | Should -Be 1
        Wait-Until { @(Get-PairAutoSyncProcess $good).Count -eq 1 } | Should -BeTrue
    }

    It "archiva un historial viejo que no es UTF-16 antes de seguir escribiendo" {
        $p = New-Pair
        $pair = New-SyncPair -Name "Log" -Source $p.Src -Backup $p.Dst
        $logDir = Join-Path $TestDrive "logs-viejos"
        New-Item -ItemType Directory $logDir -Force | Out-Null
        $log = Get-PairLogPath $pair $logDir
        [IO.File]::WriteAllText($log, "historial ANSI viejo", [Text.Encoding]::ASCII)
        $script:Started += $pair
        Start-PairAutoSync $pair $logDir | Should -BeTrue
        [IO.Path]::ChangeExtension($log, '.old.log') | Should -Exist
        Wait-Until { (Test-Path $log) -and (Get-Item $log).Length -gt 2 } | Should -BeTrue
        $fs = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
        $bytes = New-Object byte[] 2
        [void]$fs.Read($bytes, 0, 2)
        $fs.Close()
        $bytes[0] | Should -Be 0xFF
        $bytes[1] | Should -Be 0xFE
    }

    It "no archiva un historial UTF-16 válido al reiniciar (se conserva el historial)" {
        $p = New-Pair
        $pair = New-SyncPair -Name "Log2" -Source $p.Src -Backup $p.Dst
        $logDir = Join-Path $TestDrive "logs-reinicio"
        $log = Get-PairLogPath $pair $logDir
        $script:Started += $pair
        Start-PairAutoSync $pair $logDir | Should -BeTrue
        Wait-Until { (Get-Item $log).Length -gt 100 } | Should -BeTrue
        Stop-PairAutoSync $pair
        Wait-Until { @(Get-PairAutoSyncProcess $pair).Count -eq 0 } | Should -BeTrue
        $before = (Get-Item $log).Length
        Start-PairAutoSync $pair $logDir | Should -BeTrue
        [IO.Path]::ChangeExtension($log, '.old.log') | Should -Not -Exist
        (Get-Item $log).Length | Should -BeGreaterOrEqual $before
    }

    It "detecta UTF-16 con y sin BOM" {
        $a = Join-Path $TestDrive "u1.log"; [IO.File]::WriteAllBytes($a, [byte[]](0xFF, 0xFE, 0x41, 0))
        $b = Join-Path $TestDrive "u2.log"; [IO.File]::WriteAllBytes($b, [byte[]](0x0D, 0, 0x0A, 0))
        $c = Join-Path $TestDrive "u3.log"; [IO.File]::WriteAllText($c, "texto ANSI", [Text.Encoding]::ASCII)
        Test-IsUtf16File $a | Should -BeTrue
        Test-IsUtf16File $b | Should -BeTrue
        Test-IsUtf16File $c | Should -BeFalse
    }

    It "reconoce la sincronización cuando el origen es la raíz de un disco" {
        $cmd = '"C:\Windows\system32\Robocopy.exe" "C:\\" "D:\Backup C" /E /MON:1'
        Test-IsAutoSyncCommandLine $cmd "C:\" "D:\Backup C" | Should -BeTrue
        Test-IsAutoSyncCommandLine 'robocopy C:\ D:\B /E /MON:1' "C:\" "D:\B" | Should -BeTrue
    }
}

Describe "Aplicación" {
    BeforeAll {
        $script:AppPath = Join-Path $Root "src\SyncTwoFiles.ps1"
        $script:ParseErrors = $null
        $script:Ast = [System.Management.Automation.Language.Parser]::ParseFile($AppPath, [ref]$null, [ref]$script:ParseErrors)
    }

    It "el script no tiene errores de sintaxis" {
        $ParseErrors.Count | Should -Be 0
    }

    It "<_> no tiene errores de sintaxis" -ForEach "src\SyncTwoFiles.Core.psm1", "Install.ps1", "Uninstall.ps1" {
        $errs = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $Root $_), [ref]$null, [ref]$errs)
        $errs.Count | Should -Be 0
    }

    It "no tiene rutas personales escritas en el código" {
        foreach ($f in "src\SyncTwoFiles.ps1", "src\SyncTwoFiles.Core.psm1", "Install.ps1", "Uninstall.ps1") {
            Get-Content (Join-Path $Root $f) -Raw | Should -Not -Match '\\enman\\|Almacen 1|PROYECTOS DIGITALES' -Because $f
        }
    }

    It "la versión del encabezado coincide con la del módulo" {
        (Get-Content $AppPath -Raw) | Should -Match "Version\s*:\s*$([regex]::Escape((Get-AppInfo).Version))"
    }

    It "la interfaz XAML carga y tiene todos los controles" {
        if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
            Set-ItResult -Skipped -Because "WPF necesita un hilo STA"
            return
        }
        Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
        $assign = $Ast.Find({ param($n)
            $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $n.Left.Extent.Text -match 'xaml' }, $true)
        $win = [Windows.Markup.XamlReader]::Parse($assign.Right.Expression.Value)
        foreach ($n in 'PairPanel', 'BtnAdd', 'EmptyView', 'DetailView', 'BtnEdit', 'BtnLock', 'BtnDelete',
                       'BtnMin', 'BtnMax', 'BtnClose', 'AutoSwitch', 'BtnMirror', 'BtnRestore', 'Out',
                       'ConfirmPanel', 'DlgInput', 'DlgOk', 'EditorPanel', 'EdName', 'EdSrc', 'EdDst',
                       'EdPickSrc', 'EdPickDst', 'EdAuto', 'EdLock', 'EdSave') {
            $win.FindName($n) | Should -Not -BeNullOrEmpty -Because "falta el control $n"
        }
        $win.Close()
    }

    It "el icono es un .ico válido con 7 tamaños" {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $Root "assets\SyncTwoFiles.ico"))
        [BitConverter]::ToUInt16($bytes, 2) | Should -Be 1
        [BitConverter]::ToUInt16($bytes, 4) | Should -Be 7
    }
}
