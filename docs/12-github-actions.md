# Diseño de workflows

El build usa `windows-latest`, .NET estable `10.0.x`, restore, build, test y publish separados, verifica `web.config` y conserva el artifact pocos días.

El despliegue sólo parte de `main` o despacho manual. Compila fuera del servidor; el runner local descarga el artifact y llama al script confiable `C:\Deploy\Sigotest\Deploy-Sigotest.ps1`. El YAML no contiene credenciales ni modifica IIS.

Los workflows preparados no fueron ejecutados.
