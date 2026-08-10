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
- Preparar staging, commits, remotos o push solamente con autorización explícita.

## Prohibido

- Escribir en `C:\Sites\Sigotest`, `C:\Deploy\Sigotest`, IIS o cualquier ruta externa.
- Ejecutar despliegues o scripts administrativos.
- Modificar IIS, Application Pools, bindings, servicios, registro, firewall, DNS, certificados, SPN, delegación, usuarios o permisos externos.
- Instalar o registrar runners.
- Incorporar código no revisado, `.git`, workflows del repositorio de desarrollo, artifacts, secretos o configuración local durante una importación.
- Guardar tokens, credenciales, certificados o connection strings reales.
- Usar acciones GitHub de terceros sin revisión.
- Hacer commit, push, pull, merge, rebase o configurar remotos sin autorización explícita.

## Modelo de despliegue

- Compilar y probar en un runner hospedado por GitHub.
- Generar y validar `deployment-manifest.json` antes de subir el artifact.
- El runner self-hosted sólo descarga el artifact aprobado.
- El runner invoca `C:\Deploy\Sigotest\Deploy-Sigotest.ps1`; ese script vive fuera del repositorio y no se sincroniza automáticamente.
- Usar concurrency y permisos mínimos.

## Validación obligatoria

Antes de proponer un despliegue verificar build, tests, `web.config`, AspNetCoreModuleV2, manifiesto SHA-256, ausencia de secretos y diff de la importación.
