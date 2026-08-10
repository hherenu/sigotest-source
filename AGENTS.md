# Instrucciones para asistentes de IA

## Alcance

Este repositorio contiene exclusivamente el código fuente de Sigotest. No
contiene ni administra la infraestructura del servidor o de producción.

## Operaciones permitidas

- Leer, crear y modificar archivos dentro de este repositorio.
- Ejecutar Git, PowerShell y el SDK estable de .NET 10.
- Ejecutar `dotnet restore`, `build`, `test` y `publish` dentro del repositorio.
- Crear ramas, commits o interactuar con remotos solamente con autorización
  explícita del responsable técnico.

## Operaciones prohibidas

- Escribir fuera del repositorio.
- Modificar IIS, servicios, registro, firewall, DNS, certificados, SPN,
  usuarios, grupos, permisos NTFS o runners.
- Ejecutar despliegues.
- Solicitar, mostrar o almacenar contraseñas, tokens, certificados o cadenas
  de conexión reales.
- Incluir secretos en Git, logs, argumentos, capturas o artifacts.
- Usar paquetes preview, beta o release candidate.
- Incorporar Node.js, npm, Docker o servicios externos sin autorización.
- Modificar `.github/workflows` o hacer push directo a `main` sin autorización.

## Contratos obligatorios

- Framework objetivo `net10.0`.
- Autenticación Windows mediante Negotiate.
- Autorización global para usuarios autenticados.
- Acceso anónimo solamente a `GET /health` y `/Error`.
- `GET /health` debe ser una prueba de vida sin datos sensibles.
- La conexión SQL se lee desde `ConnectionStrings:SigotestDb`.
- Las credenciales nunca se agregan a `appsettings*.json`.
- `appsettings.Development.json` no se publica.
- La publicación genera `web.config` con `AspNetCoreModuleV2`.

La interfaz, las páginas y la lógica funcional pueden reemplazarse. Si un
cambio requiere alterar un contrato, detenerse y pedir autorización.

## Método de trabajo

1. Leer este archivo y `DEVELOPER.md` por completo.
2. Revisar `git status` y preservar cambios ajenos.
3. Trabajar en una rama y entregar mediante Pull Request.
4. Mantener el cambio acotado y no mezclar refactors no solicitados.
5. Agregar o actualizar pruebas para cada comportamiento relevante.
6. No hacer commit ni push salvo autorización explícita.

## Validación obligatoria

Antes de entregar:

```powershell
dotnet restore .\Sigotest.sln --configfile .\NuGet.Config
dotnet build .\Sigotest.sln --configuration Release --no-restore
dotnet test .\Sigotest.sln --configuration Release --no-build --no-restore
dotnet publish .\src\Sigotest.Web\Sigotest.Web.csproj `
  --configuration Release --no-restore --output .\artifacts\publish
```

Comprobar además que:

- `artifacts\publish\web.config` existe y contiene `AspNetCoreModuleV2`.
- `appsettings.Development.json` no fue publicado.
- no existen secretos en el diff o el artifact;
- `git diff --check` no informa errores.
