#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [ValidateRange(0,3650)][int]$RetentionDays = 0
)

$ErrorActionPreference = 'Stop'
$PoolName = 'SigotestPool'
$Destination = 'C:\Sites\Sigotest'
$BackupRoot = 'C:\Deploy\Sigotest\Backups'
$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
$ManifestPath = Join-Path $resolvedPackage 'deployment-manifest.json'
$BackupPath = Join-Path $BackupRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
$LogPath = Join-Path $BackupRoot ("runner-deploy-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
$BackupCreated = $false
$PoolStopped = $false

function Invoke-SafeRobocopy([string]$Source, [string]$Target) {
    New-Item -ItemType Directory -Force -Path $Target | Out-Null
    & robocopy.exe $Source $Target /MIR /R:2 /W:2 /NFL /NDL /NP /LOG+:$LogPath
    $robocopyExitCode = $LASTEXITCODE
    if ($robocopyExitCode -ge 8) { throw "Robocopy falló con código $robocopyExitCode." }
    $global:LASTEXITCODE = 0
}

function Wait-SigotestPoolState([string]$DesiredState, [int]$TimeoutSeconds = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $currentState = (Get-WebAppPoolState -Name $PoolName).Value
        if ($currentState -eq $DesiredState) { return }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "El Application Pool $PoolName no alcanzó el estado $DesiredState en $TimeoutSeconds segundos. Estado actual: $currentState."
}

function Stop-SigotestPool {
    $currentState = (Get-WebAppPoolState -Name $PoolName).Value
    if ($currentState -ne 'Stopped') {
        if ($currentState -ne 'Stopping') { Stop-WebAppPool -Name $PoolName }
        Wait-SigotestPoolState -DesiredState 'Stopped'
    }
    $script:PoolStopped = $true
}

function Start-SigotestPool {
    $currentState = (Get-WebAppPoolState -Name $PoolName).Value
    if ($currentState -ne 'Started') {
        if ($currentState -ne 'Starting') { Start-WebAppPool -Name $PoolName }
        Wait-SigotestPoolState -DesiredState 'Started'
    }
    $script:PoolStopped = $false
}

if (-not (Test-Path (Join-Path $resolvedPackage 'web.config'))) { throw 'El artifact no contiene web.config.' }
if (-not (Test-Path (Join-Path $resolvedPackage 'Sigotest.Web.dll'))) { throw 'El artifact no contiene Sigotest.Web.dll.' }
if (-not (Test-Path $ManifestPath)) { throw 'El artifact no contiene deployment-manifest.json.' }
if (-not (Select-String -Path (Join-Path $resolvedPackage 'web.config') -Pattern 'AspNetCoreModuleV2' -SimpleMatch)) {
    throw 'web.config no usa AspNetCoreModuleV2.'
}

$manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.application -ne 'Sigotest') { throw 'El manifiesto del artifact no es válido para Sigotest.' }
$expectedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest.files) {
    $relativePath = ([string]$entry.path).Replace('/', '\')
    if ([IO.Path]::IsPathRooted($relativePath) -or $relativePath.Split('\') -contains '..') { throw "Ruta insegura en manifiesto: $relativePath" }
    if (-not $expectedPaths.Add($relativePath)) { throw "Ruta duplicada en manifiesto: $relativePath" }
    $filePath = Join-Path $resolvedPackage $relativePath
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) { throw "Falta archivo declarado: $relativePath" }
    $file = Get-Item -LiteralPath $filePath
    if ($file.Length -ne [long]$entry.length) { throw "Tamaño inválido: $relativePath" }
    $hash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash
    if ($hash -ne [string]$entry.sha256) { throw "Hash inválido: $relativePath" }
}
$actualPaths = Get-ChildItem -LiteralPath $resolvedPackage -Recurse -File |
    Where-Object FullName -ne $ManifestPath |
    ForEach-Object { $_.FullName.Substring($resolvedPackage.Length + 1) }
$unexpected = @($actualPaths | Where-Object { -not $expectedPaths.Contains($_) })
if ($unexpected.Count) { throw "El artifact contiene archivos no declarados: $($unexpected -join ', ')" }

Import-Module WebAdministration
if (-not (Test-Path "IIS:\AppPools\$PoolName")) { throw "No existe $PoolName." }
New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null

try {
    if ((Test-Path $Destination) -and (Get-ChildItem $Destination -Force -ErrorAction SilentlyContinue)) {
        Invoke-SafeRobocopy $Destination $BackupPath
        $BackupCreated = $true
    }

    Stop-SigotestPool
    Invoke-SafeRobocopy $resolvedPackage $Destination
    Start-SigotestPool

    $health = Invoke-WebRequest -Uri 'http://localhost/health' -Headers @{ Host = 'sigotest.sbase.com.ar' } -UseDefaultCredentials -UseBasicParsing -TimeoutSec 30
    if ($health.StatusCode -ne 200) { throw "Health check devolvió $($health.StatusCode)." }

    if ($RetentionDays -gt 0) {
        $limit = (Get-Date).AddDays(-$RetentionDays)
        Get-ChildItem $BackupRoot -Directory | Where-Object LastWriteTime -lt $limit | Remove-Item -Recurse -Force
    }
} catch {
    Add-Content -Path $LogPath -Value ("{0:o} ERROR {1}" -f (Get-Date), $_.Exception.Message)
    if ($BackupCreated) {
        if (-not $PoolStopped) { Stop-SigotestPool }
        Invoke-SafeRobocopy $BackupPath $Destination
    }
    if ($PoolStopped) { Start-SigotestPool }
    throw
}
