# IIS

Diseño objetivo:

- Sitio `Sigotest` y Application Pool `SigotestPool` independientes.
- Pool en No Managed Code.
- Ruta `C:\Sites\Sigotest`.
- Binding HTTP `*:80:sigotest.sbase.com.ar`.
- Windows Authentication habilitada y Anonymous Authentication deshabilitada.
- Lectura y ejecución para `IIS AppPool\SigotestPool`.

`Default Web Site` no se modifica ni elimina. Antes de crear el binding se comprueban conflictos. HTTPS se posterga hasta disponer del certificado y política correspondiente. Los scripts en `ops` requieren revisión y PowerShell elevada; no fueron ejecutados por Codex.
