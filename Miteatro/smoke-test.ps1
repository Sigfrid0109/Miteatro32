# Compatibilidad con el nombre de prueba anterior.
& (Join-Path $PSScriptRoot 'test-system.ps1')
if (-not $?) { exit 1 }
