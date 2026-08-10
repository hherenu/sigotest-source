# GitHub Actions

`build.yml` valida PR y push en un runner hospedado: instala .NET 10 estable, restaura, compila, prueba, publica, verifica `web.config` y sube un artifact de retención corta sin secretos.

`deploy.yml` compila en runner hospedado y entrega el artifact a un runner self-hosted con labels `self-hosted`, `Windows`, `X64`, `sigotest`. El job del servidor no interpreta scripts del repositorio: invoca `C:\Deploy\Sigotest\Deploy-Sigotest.ps1`, administrado fuera del repositorio compartido.

Los permisos son de sólo lectura para contenido y `actions: write` únicamente donde se necesita subir artifacts. Concurrency evita despliegues simultáneos. Ningún workflow fue ejecutado ni se registró un runner.
