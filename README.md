# Sigotest

Aplicación web corporativa ASP.NET Core Razor Pages para `sigotest.sbase.com.ar`, operada como producto completo sobre .NET 10, autenticación integrada de Windows y despliegue controlado en IIS.

## Arquitectura y estructura

- `src/Sigotest.Web`: aplicación web `net10.0` sin base de datos, EF Core ni Identity local.
- `tests/Sigotest.Web.Tests`: pruebas de integración.
- `docs`: decisiones, procedimientos y estado.
- `ops`: scripts administrativos preparados pero no ejecutados.
- `scripts`: utilidades seguras de repositorio.
- `.github/workflows`: build y despliegue en dos etapas.

## Requisitos y versiones

- SDK estable .NET 10.0.302; runtime objetivo 10.0.10.
- Git 2.55 o posterior y PowerShell.
- Windows Authentication en el servidor IIS futuro.
- Python, Node.js, npm y Docker no son necesarios.

## Restaurar, compilar y ejecutar

```powershell
$DotnetExe = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $DotnetExe restore .\Sigotest.sln
& $DotnetExe build .\Sigotest.sln --configuration Release --no-restore
& $DotnetExe run --project .\src\Sigotest.Web\Sigotest.Web.csproj
```

## Publicar

```powershell
& $DotnetExe publish .\src\Sigotest.Web\Sigotest.Web.csproj --configuration Release --no-restore --output .\artifacts\publish
```

## Autenticación y salud

Negotiate protege todas las páginas mediante una fallback policy. Sólo `GET /health` y `/Error` son anónimos. Negotiate puede resolver Kerberos o NTLM; su nombre no demuestra qué protocolo se negoció.

`GET /health` devuelve estado, aplicación, equipo, framework y UTC sin datos de usuario ni secretos.

## Variables y seguridad

La conexión SQL futura se inyectará como `ConnectionStrings__SigotestDb`. La aplicación muestra únicamente si está configurada; nunca su contenido. Los archivos locales, certificados, secretos y outputs están excluidos de Git.

## Despliegue y pendientes

La publicación objetivo será `C:\Sites\Sigotest`, mediante `SigotestPool`, después de revisión administrativa. Quedan fuera de esta preparación: ejecutar cambios IIS, registrar SPN, validar Kerberos desde un cliente de dominio, configurar SQL Server, habilitar HTTPS/certificado, instalar el runner y conectar repositorios GitHub.

El estado vigente está en [docs/STATUS.md](docs/STATUS.md) y la ejecución administrativa en [docs/09-ejecucion-administrativa.md](docs/09-ejecucion-administrativa.md).

La separación de repositorios, carpetas y plantillas de `AGENTS.md` está documentada en [docs/17-workspaces-y-agents.md](docs/17-workspaces-y-agents.md).
