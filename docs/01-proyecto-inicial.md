# Proyecto inicial

## Estructura

- `Sigotest.sln`: solución tradicional de Visual Studio.
- `src/Sigotest.Web`: aplicación ASP.NET Core Razor Pages para .NET 10.
- `artifacts/publish`: salida local de publicación (generada, no versionada).

La aplicación usa Negotiate para autenticación integrada de Windows. Todas las páginas requieren un usuario autenticado salvo `/Error`; `GET /health` también permite acceso anónimo y devuelve un estado JSON sin secretos.

## Requisitos

- SDK estable de .NET 10.
- Windows Authentication disponible en el servidor de destino.
- PowerShell para ejecutar los ejemplos.

Python, Node.js, npm, Docker y una base de datos no son necesarios para restaurar, compilar o publicar este proyecto.

## Restaurar y compilar

Desde la raíz del repositorio:

```powershell
dotnet restore .\Sigotest.sln
dotnet build .\Sigotest.sln --configuration Release
```

## Ejecutar localmente

```powershell
dotnet run --project .\src\Sigotest.Web\Sigotest.Web.csproj
```

La autenticación Windows se realiza mediante Negotiate. La URL exacta se muestra al iniciar la aplicación. El endpoint de comprobación es `GET /health`.

## Publicar

```powershell
dotnet publish .\src\Sigotest.Web\Sigotest.Web.csproj `
  --configuration Release `
  --output .\artifacts\publish
```

La salida incluye `web.config` para un despliegue futuro en IIS. Esta etapa no configura ni modifica IIS.

## Configuración posterior

La configuración del sitio y del Application Pool de IIS, Kerberos y SQL Server se realizará posteriormente. No se deben guardar contraseñas, certificados ni cadenas de conexión reales en el repositorio.
