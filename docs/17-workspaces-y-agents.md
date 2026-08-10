# Workspaces, carpetas y AGENTS.md

## Diseño recomendado

```text
C:\Dev\Sigotest                 repositorio compartido de desarrollo
C:\Dev\Sigotest.Deploy          repositorio privado del administrador
C:\Deploy\Sigotest              estado operativo, runner, logs y backups
C:\Sites\Sigotest               publicación activa de IIS
```

Sólo las dos carpetas bajo `C:\Dev` deben abrirse como workspaces de desarrollo. `C:\Deploy` y `C:\Sites` no son repositorios ni workspaces: el administrador las modifica mediante procedimientos revisados.

## Permisos del entorno Codex

Para cada repositorio, abrir una sesión/workspace cuya raíz escribible sea exactamente su carpeta. Si Codex debe ejecutar `git add`, branch o commit, la configuración del workspace también debe permitir escritura en `.git`; `AGENTS.md` por sí solo no cambia el sandbox. Mantener lectura/escritura denegada sobre `C:\Sites`, `C:\Deploy`, IIS y configuración del sistema.

## Repositorio 1: C:\Dev\Sigotest

Destino GitHub recomendado: `https://github.com/hherenu/sigotest.git`. Es el código que puede compartirse con el desarrollador.

Antes del primer push:

1. Conservar `build.yml`.
2. No conectar un self-hosted runner a este repositorio.
3. Mover `deploy.yml` al repositorio privado cuando éste exista; después eliminarlo del compartido.
4. Mantener los scripts `ops` sólo como documentación o plantillas no ejecutables.
5. Habilitar escritura de Codex sobre `C:\Dev\Sigotest\.git` sólo si se desea que prepare Git, y autorizar commit/push explícitamente.

Contenido sugerido para `C:\Dev\Sigotest\AGENTS.md`:

```markdown
# Reglas del repositorio de desarrollo Sigotest

## Workspace

La única raíz editable es `C:\Dev\Sigotest`.

## Objetivo

Este repositorio contiene la aplicación ASP.NET Core .NET 10, pruebas, documentación y CI del producto `sigotest.sbase.com.ar`, tratado como sistema productivo completo.

## Permitido

- Crear, leer, modificar y eliminar archivos dentro del workspace.
- Ejecutar el SDK estable ubicado en `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`.
- Ejecutar restore, build, test, publish y pruebas HTTP locales.
- Ejecutar Git en modo lectura.
- Preparar staging, ramas, commits, remotos o push solamente cuando el usuario lo autorice explícitamente.
- Crear artifacts exclusivamente dentro de `C:\Dev\Sigotest\artifacts`.

## Prohibido

- Escribir fuera del workspace.
- Modificar o consultar contenido de `C:\Sites`, `C:\Deploy`, `C:\inetpub` o `C:\Windows`.
- Modificar IIS, servicios, registro, firewall, DNS, certificados, SPN, Kerberos, usuarios o permisos NTFS externos.
- Ejecutar scripts de `ops` que requieran administración.
- Instalar software, SDK, workloads, runners o herramientas.
- Usar Python, Node.js, npm, Docker o paquetes preview/beta/RC.
- Guardar secretos, tokens, certificados, contraseñas o connection strings reales.
- Conectar un self-hosted runner a este repositorio.
- Hacer commit, push, pull, merge, rebase o configurar remotos sin autorización explícita.

## Seguridad GitHub

- El workflow de desarrollo puede compilar y probar en runners hospedados.
- Este repositorio no debe desplegar directamente al servidor.
- `artifacts`, `bin`, `obj`, configuración local, secretos y certificados no se versionan.

## Validación obligatoria

Antes de declarar un cambio terminado ejecutar restore, build Release, tests y las validaciones proporcionales al cambio. Informar errores, warnings y pendientes reales.
```

## Repositorio 2: C:\Dev\Sigotest.Deploy

Debe crearse como repositorio GitHub privado y accesible solamente por administradores. No debe compartirse con el desarrollador. Contendrá el workflow de despliegue, documentación administrativa y código aprobado importado desde el repositorio de desarrollo.

Pasos:

1. Crear `C:\Dev\Sigotest.Deploy`.
2. Abrirlo como workspace independiente con escritura dentro de esa carpeta.
3. Permitir escritura en su `.git` sólo si Codex administrará Git con autorización explícita.
4. Crear un repositorio GitHub privado distinto; no reutilizar el remoto compartido.
5. Copiar allí `deploy.yml` después de revisión.
6. Importar fuentes aprobadas mediante `Import-ApprovedSource.ps1`; revisar el diff y nunca hacer commit automáticamente.
7. Asociar el self-hosted runner únicamente a este repositorio privado.

Contenido sugerido para `C:\Dev\Sigotest.Deploy\AGENTS.md`:

```markdown
# Reglas del repositorio privado de despliegue Sigotest

## Workspace

La única raíz editable es `C:\Dev\Sigotest.Deploy`.

## Objetivo

Este repositorio privado recibe código Sigotest previamente revisado, construye artifacts en runners hospedados y coordina el despliegue mediante un runner self-hosted controlado por el administrador.

## Permitido

- Crear, leer, modificar y eliminar archivos dentro del workspace.
- Preparar workflows, documentación y scripts de validación.
- Ejecutar dotnet restore, build, test y publish dentro del workspace.
- Importar una copia aprobada desde una ruta indicada expresamente por el administrador.
- Consultar Git.
- Preparar staging, commit, remoto o push solamente con autorización explícita.

## Prohibido

- Escribir en `C:\Sites\Sigotest`, `C:\Deploy\Sigotest`, IIS o cualquier ruta externa.
- Ejecutar despliegues o scripts administrativos.
- Modificar IIS, Application Pools, bindings, servicios, registro, firewall, DNS, certificados, SPN, delegación, usuarios o permisos externos.
- Instalar o registrar runners.
- Incorporar código no revisado, `.git`, workflows del repositorio de desarrollo, artifacts, secretos o configuración local durante una importación.
- Guardar tokens, credenciales, certificados o connection strings reales.
- Usar acciones GitHub aportadas por terceros sin revisión.
- Hacer commit, push, pull, merge, rebase o configurar remotos sin autorización explícita.

## Modelo de despliegue

- Compilar y probar en runner hospedado por GitHub.
- Generar y validar `deployment-manifest.json` antes de subir el artifact.
- El runner self-hosted sólo descarga el artifact aprobado.
- El runner invoca `C:\Deploy\Sigotest\Deploy-Sigotest.ps1`; ese script vive fuera del repositorio y no se sincroniza automáticamente.
- Usar concurrency y permisos mínimos.

## Validación obligatoria

Antes de proponer un despliegue verificar build, tests, web.config, AspNetCoreModuleV2, manifiesto SHA-256, ausencia de secretos y diff de la importación.
```

## C:\Deploy\Sigotest

No crear un repositorio ni abrirlo como workspace normal. El administrador debe crear manualmente:

```text
C:\Deploy\Sigotest\Deploy-Sigotest.ps1
C:\Deploy\Sigotest\Backups\
C:\Deploy\Sigotest\Logs\
```

El script se obtiene revisando `ops\Deploy-Sigotest.Template.ps1` y copiándolo manualmente. No necesita `AGENTS.md` porque Codex no debe trabajar directamente allí. Si en el futuro se autoriza una sesión administrativa separada, debe usar reglas mucho más restrictivas, aprobación por comando y backup obligatorio.

## C:\Sites\Sigotest

No crear `AGENTS.md`, repositorio ni archivos manuales. Esta carpeta es exclusivamente la publicación activa administrada por el script de despliegue. Todo cambio debe provenir de un artifact aprobado y ser reversible mediante backup.
