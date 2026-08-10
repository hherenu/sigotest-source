# Guía para el desarrollador

## Objetivo

Este repositorio es el único lugar donde se desarrolla Sigotest. La aplicación
puede evolucionar o reemplazarse por completo siempre que se mantengan los
contratos técnicos indicados en esta guía.

No se debe desarrollar en la carpeta publicada de IIS ni modificar el
repositorio administrativo de despliegue.

## Flujo de trabajo

1. Actualizar `main` y crear una rama descriptiva.
2. Implementar el cambio y sus pruebas.
3. Ejecutar restore, build, test y publish en Release.
4. Revisar el diff y confirmar que no contiene secretos.
5. Hacer push de la rama y abrir un Pull Request hacia `main`.
6. Esperar que el check `build` termine correctamente antes del merge.

Los merges a este repositorio no despliegan producción automáticamente. La
promoción y el despliegue son tareas separadas del responsable técnico.

## Contratos que no deben romperse

- Proyecto ASP.NET Core sobre `net10.0` estable.
- Windows Authentication mediante Negotiate.
- Todas las páginas protegidas salvo `GET /health` y `/Error`.
- `/health` devuelve solamente información operativa no sensible.
- La conexión se solicita como `ConnectionStrings:SigotestDb`.
- Ningún secreto se guarda en el repositorio.
- Publicación compatible con IIS y `AspNetCoreModuleV2`.
- Exclusión de `appsettings.Development.json` del artifact.
- Build y pruebas en verde.

## Configuración local

Para valores locales se puede usar configuración de usuario o variables de
entorno. Nunca copiar una credencial real a `appsettings.json`,
`appsettings.Development.json`, archivos de prueba o documentación.

La variable de entorno equivalente a la conexión es
`ConnectionStrings__SigotestDb`. No es obligatorio disponer de SQL Server para
compilar o ejecutar las pruebas existentes.

## Uso de asistentes de IA

Antes de pedir cambios a Claude, Codex u otra IA, indicarle:

```text
Leé completamente AGENTS.md y DEVELOPER.md antes de actuar. Trabajá sólo
dentro de este repositorio, preservá los contratos de infraestructura y no
uses ni solicites secretos reales.
```

El desarrollador debe revisar el diff generado por la IA y sigue siendo
responsable por el código enviado al Pull Request.

## Validación

Consultar [README.md](README.md) para los comandos completos. La entrega debe
incluir los resultados de restore, build, tests y publish, además de cualquier
advertencia pendiente.
