# Configuración segura de SQL Server

Sigotest obtiene la conexión desde `ConnectionStrings:SigotestDb`. En IIS, la clave se inyecta al proceso de `SigotestPool` mediante la variable `ConnectionStrings__SigotestDb`; no se guarda en el repositorio, el artifact, `web.config` ni `C:\Sites\Sigotest`.

## Responsabilidades

- El DBA entrega servidor, base, usuario SQL, contraseña y permisos mínimos.
- El administrador carga o rota la credencial mediante un procedimiento interactivo.
- La aplicación consume la clave de configuración y valida la conexión sin exponer sus valores.
- GitHub Actions no recibe ni materializa la credencial.

El usuario SQL no debe ser `sysadmin` y, salvo justificación, tampoco `db_owner`. Debe quedar limitado a la base y operaciones que requiera la aplicación.

## Simulación

Desde una PowerShell elevada, revisar primero:

```powershell
Set-Location C:\Dev\Sigotest

.\ops\Set-SigotestDatabaseConfiguration.ps1 `
  -SqlServer 'SERVIDOR\\INSTANCIA' `
  -Database 'BASE_SIGOTEST' `
  -SqlUser 'USUARIO_SIGOTEST' `
  -WhatIf `
  -Confirm:$false
```

La simulación no solicita la contraseña ni modifica IIS.

## Aplicación o rotación

Después de revisar el script, ejecutar sin escribir la contraseña en la línea de comandos:

```powershell
.\ops\Set-SigotestDatabaseConfiguration.ps1 `
  -SqlServer 'SERVIDOR\\INSTANCIA' `
  -Database 'BASE_SIGOTEST' `
  -SqlUser 'USUARIO_SIGOTEST' `
  -Confirm:$false
```

El script solicita la contraseña como `SecureString`, actualiza exclusivamente `ConnectionStrings__SigotestDb` en `SigotestPool` y recicla el pool. No imprime ni registra la cadena.

`Encrypt=True` y `TrustServerCertificate=False` son los valores predeterminados. `-TrustServerCertificate` sólo debe utilizarse como excepción temporal aprobada cuando SQL Server todavía no presenta un certificado confiable.

## Verificación sin revelar secretos

Comprobar la aplicación, no el valor de la variable:

```powershell
Invoke-WebRequest `
  -Uri 'http://localhost/health' `
  -Headers @{ Host = 'sigotest.sbase.com.ar' } `
  -UseBasicParsing `
  -TimeoutSec 30 |
Select-Object StatusCode, Content
```

La página principal puede indicar que la cadena está configurada, pero eso no confirma conectividad SQL. La validación real debe implementarse en la aplicación mediante una conexión breve o un health check de readiness que no publique servidor, base, usuario ni detalles de errores.

## Limitación y mejora futura

IIS almacena las variables del Application Pool en su configuración administrativa, protegida por ACL del sistema, pero no frente a administradores del servidor. Si la organización dispone de un gestor de secretos, debe preferirse y adaptarse la carga de configuración para consultarlo en tiempo de ejecución.
