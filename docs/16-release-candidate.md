# Auditoría release candidate

Fecha: 2026-08-06. SDK 10.0.302. Runtime observado: .NET 10.0.10.

## Resultado

- Restore: correcto, todos los proyectos actualizados.
- Build Release: correcto, 0 warnings y 0 errores.
- Tests: 5 aprobados, 0 fallidos.
- Publicación limpia: `artifacts/release-candidate/publish`.
- Paquete: 205 archivos y 14.605.764 bytes, incluido el manifiesto.
- Manifiesto: 204 archivos declarados, hashes SHA-256 válidos.
- `web.config`: presente y usa AspNetCoreModuleV2.
- Scripts: 9 archivos PowerShell, 0 errores de sintaxis.
- Paquetes preview/beta/RC: 0 coincidencias.
- Secretos y credenciales reales: 0 coincidencias.
- Candidatos Git prohibidos: 0.

## HTTP sobre el binario publicado

- PID temporal 1820, detenido al finalizar.
- `GET /health`: 200 y JSON esperado.
- `GET /` sin credenciales: 401.
- `GET /` con credenciales de la cuenta sandbox: 401.

La última prueba queda pendiente desde un cliente unido al dominio. No demuestra una falla de Kerberos porque la identidad aislada no tiene un contexto aceptado por Negotiate.

## Dictamen

El código es candidato válido para promoción al repositorio GitHub de desarrollo. No está autorizado todavía para despliegue productivo: faltan separar el repositorio administrativo, ejecutar los workflows reales y completar las validaciones IIS/Windows Authentication/Kerberos.
