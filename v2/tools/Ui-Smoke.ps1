# Recorre la interfaz con UI Automation y guarda capturas (prueba manual de humo).
# Usa una carpeta de datos aparte con SYNCTWOFILES_DATA para no tocar la configuración real.
param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$DataDir,
    [Parameter(Mandatory)][string]$OutDir,
    [string[]]$Steps = @('main')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
New-Item -ItemType Directory -Force $OutDir | Out-Null
$capture = Join-Path $PSScriptRoot 'Capture-Window.ps1'

$env:SYNCTWOFILES_DATA = $DataDir
$p = Start-Process $Exe -PassThru
$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline) {
    $p.Refresh()
    if ($p.MainWindowHandle -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 300
}
Start-Sleep 2
$root = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)

function Find([string]$id) {
    $cond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty), $id
    for ($i = 0; $i -lt 20; $i++) {
        $e = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
        if ($e) { return $e }
        Start-Sleep -Milliseconds 250
    }
    throw "No se encontro '$id'"
}
function Click([string]$id) {
    $e = Find $id
    $pat = $null
    if ($e.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$pat)) { $pat.Invoke() }
    elseif ($e.TryGetCurrentPattern([Windows.Automation.TogglePattern]::Pattern, [ref]$pat)) { $pat.Toggle() }
    else { throw "'$id' no se puede pulsar" }
    Start-Sleep -Milliseconds 900
}
function Shot([string]$name) { & $capture -ProcessId $p.Id -OutFile (Join-Path $OutDir "$name.png") | Out-Null }

try {
    foreach ($s in $Steps) {
        $parts = $s -split ':'
        switch ($parts[0]) {
            'shot'  { Shot $parts[1] }
            'click' { Click $parts[1] }
            'wait'  { Start-Sleep -Milliseconds ([int]$parts[1]) }
            'type'  {
                $e = Find $parts[1]
                $e.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue(($parts[2..($parts.Count - 1)] -join ':'))
                Start-Sleep -Milliseconds 400
            }
            'selectname' {
                $cond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::NameProperty), $parts[1]
                $e = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
                $walker = [Windows.Automation.TreeWalker]::ControlViewWalker
                while ($e -and $e.Current.ControlType -ne [Windows.Automation.ControlType]::ListItem) { $e = $walker.GetParent($e) }
                if (-not $e) { throw "No se encontro el elemento '$($parts[1])'" }
                $e.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
                Start-Sleep -Milliseconds 500
            }
            'clickname' {
                $cond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::NameProperty), $parts[1]
                $e = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
                if (-not $e) { throw "No se encontro '$($parts[1])'" }
                $e.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
                Start-Sleep -Milliseconds 900
            }
            'main'  { Shot 'main' }
        }
        "ok $s"
    }
}
finally {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Get-CimInstance Win32_Process -Filter "Name='robocopy.exe'" |
        Where-Object { $_.CommandLine -like "*$DataDir*" -or $_.CommandLine -like '*stf_ui*' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}
