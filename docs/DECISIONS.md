# Decisiones técnicas

## ADR-001 — .NET 10 y Razor Pages

Se conserva `net10.0` con `Microsoft.NET.Sdk.Web` y Razor Pages porque satisface la aplicación corporativa simple sin incorporar SPA, Node.js ni una base de datos prematuramente.

## ADR-002 — Autenticación Negotiate

Se usa `Microsoft.AspNetCore.Authentication.Negotiate` 10.0.10 con política fallback autenticada. Negotiate selecciona Kerberos o NTLM según el entorno; `AuthenticationType = Negotiate` no prueba por sí mismo que Kerberos haya sido utilizado.

## ADR-003 — Excepciones anónimas explícitas

Sólo `GET /health` y `/Error` permiten acceso anónimo. La política fallback protege páginas y endpoints nuevos por defecto.

## ADR-004 — Secretos fuera de Git

La conexión futura se inyectará con `ConnectionStrings__SigotestDb`. La UI comprueba únicamente presencia y nunca muestra el valor.

## ADR-005 — Separación desarrollo/despliegue

Se propone un repositorio de desarrollo sin runner ni secretos y otro administrado para despliegue. El código revisado se importa explícitamente, reduciendo la exposición del servidor a colaboradores.

## ADR-006 — Build fuera del servidor

GitHub Actions compila y publica en runner hospedado. El self-hosted runner sólo recibe un artefacto aprobado e invoca un script operativo local controlado por el administrador.

## ADR-007 — Integridad del artifact

El runner hospedado genera `deployment-manifest.json` con rutas, tamaños y SHA-256. El script operativo externo valida todos los archivos y rechaza faltantes, alteraciones, rutas inseguras y archivos extra antes de detener el Application Pool.

## ADR-008 — Sigotest es el producto en producción

`sigotest.sbase.com.ar` se diseña, protege, despliega y opera como el sistema productivo completo. No se condiciona su arquitectura a una futura promoción hacia `sigo.sbase.com.ar`. Por lo tanto, el environment GitHub `production`, el concurrency group `sigotest-production`, los backups, rollback, controles de acceso y validaciones deben considerarse definitivos para este producto.

Si en el futuro la empresa aprueba otro hostname o producto, se tratará como una decisión y proyecto nuevos, sin reservar ahora infraestructura ni complejidad para un escenario incierto.
