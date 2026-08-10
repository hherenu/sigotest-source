# Reglas de trabajo de Sigotest

## Workspace autorizado

Los archivos del proyecto se encuentran exclusivamente en:

C:\Dev\Sigotest

## Operaciones permitidas

Codex puede:

- Crear, leer, modificar y eliminar archivos dentro de C:\Dev\Sigotest.
- Ejecutar dotnet, Git y PowerShell instalados en el sistema.
- Leer información de versión y ayuda de las herramientas instaladas.
- Ejecutar dotnet restore, build, test y publish sobre este proyecto.
- Crear salidas de compilación únicamente dentro de C:\Dev\Sigotest.
- Inicializar y consultar el repositorio Git local.
- Preparar staging, crear o cambiar ramas y crear commits cuando el usuario lo autorice explícitamente.
- Configurar remotos y ejecutar fetch, pull o push cuando el usuario lo autorice explícitamente y haya indicado el repositorio de destino.

El hecho de que los ejecutables dotnet.exe, git.exe o powershell.exe estén
instalados fuera del workspace no impide ejecutarlos.

## Operaciones prohibidas

Codex no puede:

- Crear, modificar ni eliminar archivos fuera de C:\Dev\Sigotest.
- Modificar IIS.
- Modificar servicios de Windows.
- Modificar el registro de Windows.
- Modificar firewall, DNS, certificados o SPN.
- Modificar usuarios, grupos o permisos NTFS.
- Instalar software.
- Ejecutar comandos como administrador.
- Acceder al contenido de C:\Windows, C:\inetpub, C:\Sites o C:\Deploy.
- Solicitar o guardar contraseñas, tokens, certificados o cadenas de conexión.
- Usar Python, Node.js, npm, Docker o contenedores.
- Configurar remotos, hacer commit, push o pull sin autorización explícita del usuario.
- Ejecutar despliegues sin autorización explícita del usuario.
- Utilizar paquetes preview, beta o release candidate.

## Seguridad

Los secretos, credenciales y cadenas de conexión reales no deben almacenarse
en el repositorio.

Si una operación requiere modificar el servidor o escribir fuera del
workspace, Codex debe detenerse y explicarlo.

## Protocolo administrativo adicional

También se permite ejecutar `dotnet run`, realizar pruebas HTTP locales no destructivas, consultar IIS en modo lectura y crear scripts administrativos dentro del repositorio.

Sin autorización explícita está prohibido ejecutar esos scripts, detener sitios o Application Pools, instalar workloads, modificar delegación Kerberos, instalar o configurar runners, eliminar o modificar Default Web Site y ejecutar comandos destructivos.

Los scripts administrativos sólo se preparan. La entrega debe indicar al administrador el archivo y comando exactos, los cambios esperados, la verificación y la reversión.
