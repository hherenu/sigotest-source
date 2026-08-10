# Estado del proyecto

| Etapa | Estado | Observación |
|---|---|---|
| 0 — Auditoría | Completada | SDK, Git, fuentes, seguridad y artefactos inspeccionados. |
| 1 — Solución y proyecto | Completada | Solución tradicional, web y tests en `net10.0`. |
| 2 — Autenticación | Completada | Negotiate y autorización fallback; excepciones explícitas. |
| 3 — Página de prueba | Completada | Interfaz responsive, local y compilada. |
| 4 — Health check | Completada | 200 anónimo validado en Kestrel y tests. |
| 5 — Configuración | Completada | Indicador de presencia de conexión sin exponer valor. |
| 6 — Documentación | Completada | Guías técnicas, operativas y de seguimiento creadas. |
| 7 — Build y publicación | Completada | Release sin warnings; publicación validada. |
| 8 — Prueba local | Bloqueada | Salud y 401 validados; credenciales de la cuenta aislada no fueron aceptadas. |
| 9 — Scripts IIS | Requiere acción administrativa | Cuatro scripts preparados y sintaxis validada; no ejecutados. |
| 10 — Kerberos | Requiere acción administrativa | Diagnóstico de sólo lectura preparado; SPN sin modificar. |
| 11 — Git local | Bloqueada | Preflight correcto; `.git` es sólo lectura y falta definir el rol del repositorio GitHub. |
| 12 — GitHub Free | Completada | Arquitectura de dos repositorios documentada. |
| 13 — GitHub Actions | Requiere acción administrativa | Build, tests, publish y manifiesto SHA-256 preparados; workflows no ejecutados y runner no instalado. |
| 14 — Pruebas | Completada | 5/5 pruebas pasan en Release. |
| 16 — Release candidate | Completada | Restore, build, tests, publish, manifiesto, seguridad y HTTP validados. |
| IIS/HTTPS/SQL Server | Requiere acción administrativa | Pendiente de revisión y ejecución posterior. |
