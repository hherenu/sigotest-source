#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$SiteName = 'Sigotest'
$PoolName = 'SigotestPool'
$HostName = 'sigotest.sbase.com.ar'
Import-Module WebAdministration

$site = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
$pool = Get-Item "IIS:\AppPools\$PoolName" -ErrorAction SilentlyContinue
[pscustomobject]@{
    SiteExists = [bool]$site
    SiteState = if ($site) { $site.State } else { $null }
    PoolExists = [bool]$pool
    PoolState = if ($pool) { (Get-WebAppPoolState $PoolName).Value } else { $null }
    PhysicalPath = if ($site) { $site.PhysicalPath } else { $null }
    Bindings = if ($site) { ($site.Bindings.Collection.bindingInformation -join '; ') } else { $null }
    WindowsAuthentication = (Get-WebConfigurationProperty -PSPath IIS:\ -Location $SiteName -Filter 'system.webServer/security/authentication/windowsAuthentication' -Name enabled).Value
    AnonymousAuthentication = (Get-WebConfigurationProperty -PSPath IIS:\ -Location $SiteName -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name enabled).Value
    WebConfigExists = Test-Path 'C:\Sites\Sigotest\web.config'
    AspNetCoreModuleV2 = [bool](Get-WebGlobalModule -Name AspNetCoreModuleV2 -ErrorAction SilentlyContinue)
    DotNet10Runtime = [bool]((& dotnet --list-runtimes) -match 'Microsoft\.AspNetCore\.App 10\.')
}

& icacls.exe 'C:\Sites\Sigotest'
Invoke-WebRequest -Uri 'http://localhost/health' -Headers @{ Host = $HostName } -UseDefaultCredentials -TimeoutSec 30 | Select-Object StatusCode, Content
Invoke-WebRequest -Uri 'http://localhost/' -Headers @{ Host = $HostName } -UseDefaultCredentials -TimeoutSec 30 | Select-Object StatusCode
