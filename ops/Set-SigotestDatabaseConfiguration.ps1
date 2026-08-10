#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$SqlServer,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Database,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$SqlUser,
    [switch]$TrustServerCertificate
)

$ErrorActionPreference = 'Stop'
$PoolName = 'SigotestPool'
$VariableName = 'ConnectionStrings__SigotestDb'

function Wait-SigotestPoolState([string]$DesiredState, [int]$TimeoutSeconds = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $currentState = (Get-WebAppPoolState -Name $PoolName).Value
        if ($currentState -eq $DesiredState) { return }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "El Application Pool $PoolName no alcanzó el estado $DesiredState en $TimeoutSeconds segundos. Estado actual: $currentState."
}

Import-Module WebAdministration
if (-not (Test-Path "IIS:\AppPools\$PoolName")) {
    throw "No existe el Application Pool $PoolName."
}

if (-not $PSCmdlet.ShouldProcess(
    "IIS Application Pool $PoolName",
    "Configurar $VariableName y reciclar el pool"
)) {
    Write-Output 'Simulación completada. No se solicitó ni almacenó ninguna contraseña.'
    return
}

$securePassword = Read-Host "Contraseña SQL para $SqlUser" -AsSecureString
if ($securePassword.Length -eq 0) { throw 'La contraseña no puede estar vacía.' }

$plainPassword = $null
$connectionString = $null
$serverManager = $null
try {
    $credential = [PSCredential]::new($SqlUser, $securePassword)
    $plainPassword = $credential.GetNetworkCredential().Password

    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder['Data Source'] = $SqlServer
    $builder['Initial Catalog'] = $Database
    $builder['User ID'] = $SqlUser
    $builder['Password'] = $plainPassword
    $builder['Encrypt'] = $true
    $builder['TrustServerCertificate'] = [bool]$TrustServerCertificate
    $builder['Persist Security Info'] = $false
    $builder['Connect Timeout'] = 15
    $connectionString = $builder.ConnectionString

    $administrationAssembly = Join-Path $env:windir 'System32\inetsrv\Microsoft.Web.Administration.dll'
    if (-not (Test-Path -LiteralPath $administrationAssembly -PathType Leaf)) {
        throw 'No se encontró Microsoft.Web.Administration.dll. Verifique la instalación de IIS Management Scripts and Tools.'
    }
    Add-Type -Path $administrationAssembly
    $serverManager = [Microsoft.Web.Administration.ServerManager]::new()
    $configuration = $serverManager.GetApplicationHostConfiguration()
    $section = $configuration.GetSection('system.applicationHost/applicationPools')
    $pool = $section.GetCollection() |
        Where-Object { $_.GetAttributeValue('name') -eq $PoolName } |
        Select-Object -First 1
    if (-not $pool) { throw "No se encontró $PoolName en ApplicationHost.config." }

    $variables = $pool.GetCollection('environmentVariables')
    $variable = $variables |
        Where-Object { $_.GetAttributeValue('name') -eq $VariableName } |
        Select-Object -First 1

    if (-not $variable) {
        $variable = $variables.CreateElement('add')
        $variable.SetAttributeValue('name', $VariableName)
        $variables.Add($variable)
    }
    $variable.SetAttributeValue('value', $connectionString)
    $serverManager.CommitChanges()

    $state = (Get-WebAppPoolState -Name $PoolName).Value
    if ($state -ne 'Stopped') {
        if ($state -ne 'Stopping') { Stop-WebAppPool -Name $PoolName }
        Wait-SigotestPoolState -DesiredState 'Stopped'
    }
    Start-WebAppPool -Name $PoolName
    Wait-SigotestPoolState -DesiredState 'Started'

    [pscustomobject]@{
        ApplicationPool = $PoolName
        ConfigurationKey = $VariableName
        Configured = $true
        PoolState = (Get-WebAppPoolState -Name $PoolName).Value
        Encrypt = $true
        TrustServerCertificate = [bool]$TrustServerCertificate
    }
} finally {
    if ($serverManager) { $serverManager.Dispose() }
    if ($builder) { $builder.Clear() }
    $connectionString = $null
    $plainPassword = $null
    $securePassword.Dispose()
}
