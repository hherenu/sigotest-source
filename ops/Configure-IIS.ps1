#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param()

$ErrorActionPreference = 'Stop'
$SiteName = 'Sigotest'
$PoolName = 'SigotestPool'
$HostName = 'sigotest.sbase.com.ar'
$SitePath = 'C:\Sites\Sigotest'
$BackupPath = 'C:\Deploy\Sigotest\Backups'
$LogPath = 'C:\Deploy\Sigotest\Logs\Configure-IIS.log'

function Invoke-ConfigurationChange {
    param(
        [Parameter(Mandatory)][string]$Target,
        [Parameter(Mandatory)][string]$Action,
        [Parameter(Mandatory)][scriptblock]$Operation
    )
    if ($PSCmdlet.ShouldProcess($Target, $Action)) { & $Operation }
}

function Write-Log([string]$Message) {
    if (-not $WhatIfPreference) {
        "{0:o} {1}" -f (Get-Date), $Message | Add-Content -LiteralPath $LogPath
    }
}

$os = Get-CimInstance Win32_OperatingSystem
if ($os.ProductType -eq 1) { throw 'Se requiere Windows Server.' }

$iisFeature = Get-WindowsFeature Web-Server
$authFeature = Get-WindowsFeature Web-Windows-Auth
if (-not $iisFeature.Installed) { throw 'IIS no está instalado.' }
if (-not $authFeature.Installed) { throw 'Web-Windows-Auth no está instalado.' }

Import-Module WebAdministration
if (-not (Get-WebGlobalModule -Name AspNetCoreModuleV2 -ErrorAction SilentlyContinue)) {
    throw 'AspNetCoreModuleV2 no está registrado.'
}

$dotnetCommand = Get-Command dotnet.exe -ErrorAction Stop
$runtimeOutput = & $dotnetCommand.Source --list-runtimes
if (-not ($runtimeOutput -match 'Microsoft\.AspNetCore\.App 10\.')) { throw 'No se encontró ASP.NET Core Runtime 10.' }

$conflictingSites = @(
    Get-Website | Where-Object Name -ne $SiteName | Where-Object {
        $_.Bindings.Collection | Where-Object {
            $_.protocol -eq 'http' -and $_.bindingInformation -eq "*:80:$HostName"
        }
    }
)
if ($conflictingSites.Count) {
    throw "El binding *:80:$HostName ya pertenece a: $($conflictingSites.Name -join ', ')."
}

foreach ($path in @($SitePath, $BackupPath, (Split-Path $LogPath -Parent))) {
    if (-not (Test-Path -LiteralPath $path)) {
        Invoke-ConfigurationChange $path 'Crear directorio' {
            New-Item -ItemType Directory -Path $path -Force | Out-Null
        }
    }
}

$poolExists = Test-Path "IIS:\AppPools\$PoolName"
if (-not $poolExists) {
    Invoke-ConfigurationChange $PoolName 'Crear Application Pool' {
        New-WebAppPool -Name $PoolName | Out-Null
    }
}

Invoke-ConfigurationChange $PoolName 'Configurar No Managed Code' {
    Set-ItemProperty "IIS:\AppPools\$PoolName" -Name managedRuntimeVersion -Value ''
}
Invoke-ConfigurationChange $PoolName 'Configurar ApplicationPoolIdentity' {
    Set-ItemProperty "IIS:\AppPools\$PoolName" -Name processModel.identityType -Value ApplicationPoolIdentity
}

$siteExists = Test-Path "IIS:\Sites\$SiteName"
if (-not $siteExists) {
    Invoke-ConfigurationChange $SiteName 'Crear sitio IIS y binding HTTP' {
        New-Website -Name $SiteName -PhysicalPath $SitePath -ApplicationPool $PoolName -Port 80 -HostHeader $HostName | Out-Null
    }
} else {
    Invoke-ConfigurationChange $SiteName "Asignar physicalPath $SitePath" {
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $SitePath
    }
    Invoke-ConfigurationChange $SiteName "Asignar Application Pool $PoolName" {
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $PoolName
    }
    if (-not (Get-WebBinding -Name $SiteName -Protocol http | Where-Object bindingInformation -eq "*:80:$HostName")) {
        Invoke-ConfigurationChange $SiteName "Crear binding *:80:$HostName" {
            New-WebBinding -Name $SiteName -Protocol http -Port 80 -HostHeader $HostName
        }
    }
}

Invoke-ConfigurationChange $SiteName 'Habilitar Windows Authentication' {
    Set-WebConfigurationProperty -PSPath IIS:\ -Location $SiteName -Filter 'system.webServer/security/authentication/windowsAuthentication' -Name enabled -Value $true
}
Invoke-ConfigurationChange $SiteName 'Habilitar Anonymous Authentication para autorizacion administrada por ASP.NET Core' {
    Set-WebConfigurationProperty -PSPath IIS:\ -Location $SiteName -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name enabled -Value $true
}

foreach ($anonymousPath in @('health', 'Error')) {
    $location = "$SiteName/$anonymousPath"
    if (Get-WebConfigurationLocation -PSPath IIS:\ -Name $location -ErrorAction SilentlyContinue) {
        Invoke-ConfigurationChange $location 'Eliminar excepción IIS heredada; ASP.NET Core administra el acceso anónimo' {
            Remove-WebConfigurationLocation -PSPath IIS:\ -Name $location -Confirm:$false
        }
    }
}

Invoke-ConfigurationChange $SitePath "Conceder lectura y ejecución a IIS AppPool\$PoolName" {
    & icacls.exe $SitePath /grant "IIS AppPool\${PoolName}:(OI)(CI)(RX)" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls falló con código $LASTEXITCODE." }
}

Write-Log "Configuración verificada para $SiteName. Default Web Site no fue modificado."
Write-Output "Configuración preparada para $SiteName en $HostName. WhatIf=$WhatIfPreference"
