#Requires -RunAsAdministrator
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BackupPath)

$ErrorActionPreference = 'Stop'
$PoolName = 'SigotestPool'
$Destination = 'C:\Sites\Sigotest'
$BackupRoot = 'C:\Deploy\Sigotest\Backups'
$resolvedRoot = (Resolve-Path $BackupRoot).Path
$resolvedBackup = (Resolve-Path -LiteralPath $BackupPath).Path
if (-not $resolvedBackup.StartsWith($resolvedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'El backup debe pertenecer al directorio de backups de Sigotest.' }

Get-ChildItem $BackupRoot -Directory | Select-Object FullName, LastWriteTime
$confirmation = Read-Host "Escriba RESTAURAR para confirmar $resolvedBackup"
if ($confirmation -ne 'RESTAURAR') { throw 'Rollback cancelado.' }

$LogPath = Join-Path $BackupRoot ("rollback-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
Import-Module WebAdministration
Stop-WebAppPool -Name $PoolName
try {
    & robocopy.exe $resolvedBackup $Destination /MIR /R:2 /W:2 /LOG:$LogPath
    if ($LASTEXITCODE -ge 8) { throw "Robocopy falló con código $LASTEXITCODE." }
} finally {
    Start-WebAppPool -Name $PoolName
}
$health = Invoke-WebRequest -Uri 'http://localhost/health' -Headers @{ Host = 'sigotest.sbase.com.ar' } -UseDefaultCredentials -TimeoutSec 30
if ($health.StatusCode -ne 200) { throw "Health check posterior al rollback: $($health.StatusCode)." }
