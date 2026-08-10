# Sigotest

Código fuente de la aplicación web corporativa Sigotest, desarrollada con
ASP.NET Core Razor Pages sobre .NET 10.

Este repositorio contiene únicamente la aplicación, sus pruebas y la
integración continua. La configuración de IIS, las credenciales, el runner y
el despliegue se administran fuera de este repositorio.

## Requisitos

- SDK estable de .NET 10.
- Git.
- Windows para comprobar autenticación integrada fuera de las pruebas.

No se requieren Python, Node.js, npm, Docker ni una base de datos para
compilar y ejecutar las pruebas.

## Validación local

```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dotnet restore .\Sigotest.sln --configfile .\NuGet.Config
& $dotnet build .\Sigotest.sln --configuration Release --no-restore
& $dotnet test .\Sigotest.sln --configuration Release --no-build --no-restore
& $dotnet publish .\src\Sigotest.Web\Sigotest.Web.csproj `
  --configuration Release `
  --no-restore `
  --output .\artifacts\publish
```

## Contratos de infraestructura

- Todas las páginas requieren autenticación Windows mediante Negotiate.
- `GET /health` y `/Error` permiten acceso anónimo.
- La conexión SQL se obtiene de `ConnectionStrings:SigotestDb`; nunca se
  almacena en Git.
- La publicación para IIS debe generar `web.config` con
  `AspNetCoreModuleV2`.
- `appsettings.Development.json` no debe formar parte de la publicación.

Antes de desarrollar, leer [DEVELOPER.md](DEVELOPER.md). Si se utiliza una IA,
también debe leer y respetar [AGENTS.md](AGENTS.md).
