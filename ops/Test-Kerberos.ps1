[CmdletBinding()]
param([string]$HostName = 'sigotest.sbase.com.ar')

$ErrorActionPreference = 'Continue'
$SiteName = 'Sigotest'
$PoolName = 'SigotestPool'
$shortServer = $env:COMPUTERNAME

Import-Module WebAdministration -ErrorAction Stop
$pool = Get-Item "IIS:\AppPools\$PoolName" -ErrorAction SilentlyContinue
[pscustomobject]@{
    HostName = $HostName
    ShortServerName = $shortServer
    PoolIdentityType = $pool.processModel.identityType
    PoolUserName = $pool.processModel.userName
    WindowsAuthentication = (Get-WebConfigurationProperty -PSPath IIS:\ -Location $SiteName -Filter 'system.webServer/security/authentication/windowsAuthentication' -Name enabled).Value
    Providers = ((Get-WebConfiguration -PSPath IIS:\ -Location $SiteName -Filter 'system.webServer/security/authentication/windowsAuthentication/providers/add').Collection.value -join ', ')
}

& setspn.exe -Q "HTTP/$HostName"
& setspn.exe -Q "HTTP/$shortServer"
& klist.exe

Get-WinEvent -FilterHashtable @{ LogName = 'System'; StartTime = (Get-Date).AddHours(-4) } -ErrorAction SilentlyContinue |
    Where-Object ProviderName -Match 'IIS|Kerberos|LsaSrv' |
    Select-Object -First 50 TimeCreated, ProviderName, Id, LevelDisplayName, Message

Write-Host 'Revise duplicados en las salidas setspn. Este script no crea, elimina ni modifica SPN o delegación.'
