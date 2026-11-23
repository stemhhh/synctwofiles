# Ejecuta las pruebas: powershell -STA -ExecutionPolicy Bypass -File Run-Tests.ps1
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Import-Module Pester -MinimumVersion 5.5
$c = New-PesterConfiguration
$c.Run.Path = Join-Path $PSScriptRoot 'tests'
$c.Output.Verbosity = 'Detailed'
$c.Run.Exit = $true
Invoke-Pester -Configuration $c
