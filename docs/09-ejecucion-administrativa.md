# Ejecución administrativa

Revisar primero `ops/Configure-IIS.ps1`, `Deploy-Local.ps1`, `Verify-IIS.ps1` y `Rollback-IIS.ps1`. Desde PowerShell elevada:

```powershell
Set-Location C:\Dev\Sigotest
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File .\ops\Configure-IIS.ps1
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File .\ops\Deploy-Local.ps1
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File .\ops\Verify-IIS.ps1
```

El primer comando crea/verifica `SigotestPool`, el sitio `Sigotest`, carpetas, binding, autenticación y permisos. No toca Default Web Site, DNS, HTTPS ni SPN. Verificar con `Verify-IIS.ps1`, IIS Manager y acceso autenticado.

El despliegue publica, crea backup, detiene sólo `SigotestPool`, copia, inicia y prueba salud. Ante fallo intenta restaurar el backup.

Para rollback, seleccionar explícitamente una ruta mostrada por el administrador:

```powershell
Get-ChildItem C:\Deploy\Sigotest\Backups -Directory
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File .\ops\Rollback-IIS.ps1 -BackupPath 'C:\Deploy\Sigotest\Backups\AAAAMMDD-HHMMSS'
```

El rollback reemplaza la publicación con el backup elegido, reinicia sólo el pool y valida `/health`. Para revertir la configuración inicial, detener y eliminar exclusivamente el sitio `Sigotest` y `SigotestPool` después de confirmar que no sirven otras aplicaciones; conservar o archivar las carpetas según política. Esa reversión no está automatizada para evitar borrados accidentales.

## Script usado por el runner

`ops/Deploy-Sigotest.Template.ps1` es una plantilla para revisión. Después de aprobarla, el administrador debe copiarla manualmente fuera del repositorio como:

```powershell
Copy-Item .\ops\Deploy-Sigotest.Template.ps1 C:\Deploy\Sigotest\Deploy-Sigotest.ps1
```

Esa copia externa es la que invoca GitHub Actions con `-PackagePath`. No debe sincronizarse automáticamente desde el repositorio compartido: cualquier actualización requiere revisión administrativa. La plantilla despliega el artifact recibido y no recompila código en el servidor.
