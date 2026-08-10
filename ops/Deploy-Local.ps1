#Requires -RunAsAdministrator
[CmdletBinding()]
param([ValidateRange(0,3650)][int]$RetentionDays = 0)

$ErrorActionPreference = 'Stop'
$PoolName = 'SigotestPool'
$Project = 'C:\Dev\Sigotest\src\Sigotest.Web\Sigotest.Web.csproj'
$Workspace = 'C:\Dev\Sigotest'
$ArtifactPath = Join-Path $Workspace 'artifacts\publish'
$Destination = 'C:\Sites\Sigotest'
$BackupRoot = 'C:\Deploy\Sigotest\Backups'
$BackupPath = Join-Path $BackupRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
$LogPath = Join-Path $BackupRoot ("deploy-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
$DotnetExe = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
$PoolStopped = $false
$BackupCreated = $false

function Invoke-Robocopy([string]$Source, [string]$Target, [string[]]$Extra = @()) {
    New-Item -ItemType Directory -Force -Path $Target | Out-Null
    & robocopy.exe $Source $Target /MIR /R:2 /W:2 /NFL /NDL /NP @Extra /LOG+:$LogPath
    if ($LASTEXITCODE -ge 8) { throw "Robocopy falló con código $LASTEXITCODE." }
}

function Restore-Backup {
    if ($BackupCreated -and (Test-Path $BackupPath)) {
        Invoke-Robocopy $BackupPath $Destination
        Start-WebAppPool -Name $PoolName
        $script:PoolStopped = $false
    }
}

try {
    if (-not (Test-Path $DotnetExe)) { throw "No se encontró el SDK en $DotnetExe." }
    Import-Module WebAdministration
    if (-not (Test-Path "IIS:\AppPools\$PoolName")) { throw "No existe $PoolName." }

    & $DotnetExe publish $Project --configuration Release --output $ArtifactPath
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falló.' }
    if (-not (Test-Path (Join-Path $ArtifactPath 'web.config'))) { throw 'La publicación no contiene web.config.' }

    New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null
    if ((Test-Path $Destination) -and (Get-ChildItem $Destination -Force -ErrorAction SilentlyContinue)) {
        Invoke-Robocopy $Destination $BackupPath
        $BackupCreated = $true
    }

    Stop-WebAppPool -Name $PoolName
    $PoolStopped = $true
    Invoke-Robocopy $ArtifactPath $Destination
    Start-WebAppPool -Name $PoolName
    $PoolStopped = $false

    $health = Invoke-WebRequest -Uri 'http://localhost/health' -Headers @{ Host = 'sigotest.sbase.com.ar' } -UseDefaultCredentials -TimeoutSec 30
    if ($health.StatusCode -ne 200) { throw "Health check devolvió $($health.StatusCode)." }

    if ($RetentionDays -gt 0) {
        $limit = (Get-Date).AddDays(-$RetentionDays)
        Get-ChildItem $BackupRoot -Directory | Where-Object LastWriteTime -lt $limit | Remove-Item -Recurse -Force
    }
} catch {
    if ($PoolStopped -or $BackupCreated) { Restore-Backup }
    Add-Content -Path $LogPath -Value ("{0:o} ERROR {1}" -f (Get-Date), $_.Exception.Message)
    throw
}
