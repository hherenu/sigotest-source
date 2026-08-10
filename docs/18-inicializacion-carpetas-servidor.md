# Inicialización de carpetas del servidor

El script `ops/Initialize-ServerFolders.ps1` prepara de forma idempotente:

```text
C:\Deploy\Sigotest\
├── Deploy-Sigotest.ps1
├── Backups\
└── Logs\

C:\Sites\Sigotest\
```

Debe ejecutarse desde una PowerShell elevada. Primero simular:

```powershell
Set-Location C:\Dev\Sigotest
.\ops\Initialize-ServerFolders.ps1 -WhatIf
```

Después de revisar la simulación:

```powershell
.\ops\Initialize-ServerFolders.ps1
```

Si ya existe el script operativo, no se sobrescribe. Para actualizarlo tras una revisión explícita:

```powershell
.\ops\Initialize-ServerFolders.ps1 -UpdateDeployScript
```

La inicialización no modifica IIS ni ACL. Los permisos de lectura de `IIS AppPool\SigotestPool` se aplican posteriormente con `Configure-IIS.ps1`. La identidad y carpeta de trabajo del runner se definen durante su instalación; no se otorgan permisos amplios o genéricos.
