#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$DeployScriptSource = 'C:\Dev\Sigotest\ops\Deploy-Sigotest.Template.ps1',
    [switch]$UpdateDeployScript
)

$ErrorActionPreference = 'Stop'
$DeployRoot = 'C:\Deploy\Sigotest'
$BackupRoot = Join-Path $DeployRoot 'Backups'
$LogRoot = Join-Path $DeployRoot 'Logs'
$SiteRoot = 'C:\Sites\Sigotest'
$DeployScriptDestination = Join-Path $DeployRoot 'Deploy-Sigotest.ps1'

function Assert-SafePath([string]$Path, [string]$ExpectedPath) {
    if (-not [IO.Path]::GetFullPath($Path).Equals([IO.Path]::GetFullPath($ExpectedPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Ruta inesperada: $Path"
    }
    if (Test-Path -LiteralPath $Path) {
        $item = Get-Item -LiteralPath $Path -Force
        if (-not $item.PSIsContainer) { throw "La ruta existe pero no es un directorio: $Path" }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "No se admite un reparse point: $Path" }
    }
}

$os = Get-CimInstance Win32_OperatingSystem
if ($os.ProductType -eq 1) { throw 'Este script está destinado a Windows Server.' }

Assert-SafePath $DeployRoot 'C:\Deploy\Sigotest'
Assert-SafePath $SiteRoot 'C:\Sites\Sigotest'

foreach ($path in @($DeployRoot, $BackupRoot, $LogRoot, $SiteRoot)) {
    if (-not (Test-Path -LiteralPath $path)) {
        if ($PSCmdlet.ShouldProcess($path, 'Crear directorio')) {
            New-Item -ItemType Directory -Path $path -Force | Out-Null
        }
    }
}

if (-not (Test-Path -LiteralPath $DeployScriptSource -PathType Leaf)) {
    throw "No existe la plantilla de despliegue: $DeployScriptSource"
}

if ((Test-Path -LiteralPath $DeployScriptDestination) -and -not $UpdateDeployScript) {
    Write-Warning "Ya existe $DeployScriptDestination. No se sobrescribió; use -UpdateDeployScript después de revisarlo."
} elseif ($PSCmdlet.ShouldProcess($DeployScriptDestination, 'Copiar script operativo revisado')) {
    Copy-Item -LiteralPath $DeployScriptSource -Destination $DeployScriptDestination -Force:$UpdateDeployScript
    $sourceHash = (Get-FileHash -LiteralPath $DeployScriptSource -Algorithm SHA256).Hash
    $destinationHash = (Get-FileHash -LiteralPath $DeployScriptDestination -Algorithm SHA256).Hash
    if ($sourceHash -ne $destinationHash) { throw 'El hash del script copiado no coincide con la plantilla.' }
}

[pscustomobject]@{
    DeployRoot = $DeployRoot
    Backups = $BackupRoot
    Logs = $LogRoot
    SiteRoot = $SiteRoot
    DeployScript = $DeployScriptDestination
    DeployScriptExists = Test-Path -LiteralPath $DeployScriptDestination -PathType Leaf
}

Write-Host ''
Write-Host 'No se modificaron IIS, Application Pools, bindings, autenticación, SPN ni permisos NTFS.'
Write-Host 'Los permisos se aplicarán cuando estén definidas las identidades de SigotestPool y del runner.'
