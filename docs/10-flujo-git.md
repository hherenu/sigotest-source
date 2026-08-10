# Flujo Git

- `main`: código aprobado y liberable.
- `dev`: integración previa.
- `feature/*`: cambios acotados mediante Pull Request.

Los PR requieren revisión manual y build exitoso antes del merge. Un rollback de código se hace revirtiendo el cambio mediante otro PR; el rollback operativo usa backups publicados.

No se versionan secretos, certificados, configuración local, artifacts, `bin` ni `obj`. El desarrollador trabaja sólo en el repositorio de desarrollo, sin acceso al servidor, IIS, runner, repositorio de despliegue o credenciales. El administrador importa únicamente cambios revisados mediante `scripts/Import-ApprovedSource.ps1`.

El repositorio local ya está inicializado. No se creó `origin`, ramas, commits ni pushes durante esta preparación.
