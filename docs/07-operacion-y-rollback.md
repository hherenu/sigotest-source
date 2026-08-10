# Operación y rollback

Flujo de despliegue: publicar en staging, validar el paquete, crear backup con sello temporal, detener sólo `SigotestPool`, copiar con robocopy, iniciar el pool y consultar `/health`. Los códigos robocopy 0–7 son éxito; 8 o superior implican fallo.

Antes del backup se valida `deployment-manifest.json`: aplicación, versión de esquema, rutas relativas seguras, tamaños y SHA-256 de todos los archivos. También se rechazan archivos no declarados.

La validación local o en CI no requiere privilegios administrativos:

```powershell
.\scripts\Test-DeploymentManifest.ps1 -PackagePath .\artifacts\publish
```

Ante un fallo posterior a la detención se restaura el backup y se comprueba nuevamente la salud. Los backups no se eliminan salvo que el administrador configure una retención explícita.

`Rollback-IIS.ps1` exige una ruta de backup explícita; nunca elige automáticamente. Todos los scripts generan logs y usan `ErrorActionPreference = Stop`.
