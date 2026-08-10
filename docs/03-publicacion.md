# Publicación

Desde la raíz:

```powershell
$DotnetExe = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $DotnetExe restore .\Sigotest.sln
& $DotnetExe build .\Sigotest.sln --configuration Release --no-restore
& $DotnetExe publish .\src\Sigotest.Web\Sigotest.Web.csproj --configuration Release --no-restore --output .\artifacts\publish
```

Validar `Sigotest.Web.dll`, `.deps.json`, `.runtimeconfig.json`, `wwwroot` y `web.config`. Este último debe referenciar `AspNetCoreModuleV2`. `artifacts` es efímero y no se versiona.

La copia a `C:\Sites\Sigotest` sólo debe realizarse con el procedimiento administrativo, backup previo, detención exclusiva de `SigotestPool`, health check y rollback ante fallo.
