# Preflight GitHub y límites de carpetas

## Qué se versiona

El repositorio de desarrollo contiene:

- solución, aplicación .NET y pruebas;
- configuración no sensible;
- documentación técnica;
- scripts que pueden ser revisados, pero no ejecutados automáticamente en el servidor;
- workflow de build.

`bin`, `obj`, artifacts, configuración local, certificados y secretos están excluidos.

## Qué no se versiona

- `C:\Sites\Sigotest`: publicación activa administrada por IIS.
- `C:\Deploy\Sigotest`: backups, logs, runner y script operativo confiable.
- tokens, credenciales, connection strings y certificados reales.

Estas rutas no deben convertirse en subcarpetas del repositorio.

## Repositorios recomendados

1. `sigotest` o `sigotest-desarrollo`: aplicación compartida y CI, sin runner del servidor.
2. `sigotest-deploy`: repositorio exclusivo del administrador, con código aprobado y workflow de despliegue.

Una ubicación local posible para el segundo repositorio es `C:\Dev\Sigotest.Deploy`, pero debe abrirse como workspace independiente y tener su propio `AGENTS.md`. El script operativo real permanece fuera de ambos repositorios en `C:\Deploy\Sigotest\Deploy-Sigotest.ps1`.

## Decisión previa al push

Antes de publicar este repositorio debe definirse si `https://github.com/hherenu/sigotest.git` será:

- el repositorio compartido de desarrollo, en cuyo caso `deploy.yml` debería promoverse sólo al repositorio administrativo; o
- el repositorio administrativo de despliegue, en cuyo caso no debe compartirse con el desarrollador.

No debe conectarse un self-hosted runner a un repositorio donde terceros puedan modificar workflows sin un proceso de promoción controlado.

## Comprobaciones del primer push

```powershell
git status --short
git check-ignore artifacts src/Sigotest.Web/bin src/Sigotest.Web/obj .env appsettings.Production.json
git add --dry-run .
git diff --cached --check
```

Después de revisar el staging, y sólo con autorización explícita, se puede crear el commit, renombrar la rama a `main`, agregar `origin` y hacer push.

## Resultado del preflight local

- 117 archivos candidatos a versionar.
- 0 candidatos prohibidos o ignorados incorrectamente.
- `artifacts`, `bin`, `obj`, `.env`, configuraciones locales y certificados están ignorados.
- Rama actual: `master`.
- No hay remotos configurados.
- El entorno actual monta `.git` como sólo lectura; incluso `git add --dry-run` no puede crear `.git/index.lock`. Para preparar commits desde Codex se debe habilitar escritura sobre `.git` en un workspace autorizado, además de dar autorización explícita para commit/push.
- Sigue pendiente decidir si `hherenu/sigotest` será el repositorio compartido de desarrollo o el repositorio administrativo de despliegue.
