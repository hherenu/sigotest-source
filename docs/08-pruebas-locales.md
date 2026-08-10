# Pruebas locales

Fecha: 2026-08-06. Servidor temporal: Kestrel en `http://127.0.0.1:5100`. PID 7392, detenido al finalizar.

| Prueba | Resultado |
|---|---|
| `GET /health` anónimo | 200; JSON con `status=ok`, `application=Sigotest`, equipo, `.NET 10.0.10` y UTC. |
| `GET /` sin credenciales | 401, comportamiento esperado. |
| `GET /` con `-UseDefaultCredentials` | 401; la cuenta aislada `sbsigotest\codexsandboxoffline` no obtuvo una identidad aceptada por Negotiate. |
| HTML autenticado | No disponible por la limitación anterior. La vista fue compilada en Release y su contenido se verifica por código. |
| Proceso | Detenido mediante `finally`; no quedó la instancia iniciada para la prueba. |

El logger Event Log se deshabilitó solamente mediante variable del proceso de prueba porque la cuenta aislada no puede escribir en Windows Event Log. No se cambió la configuración del servidor ni de la aplicación.

La validación autenticada completa debe repetirse luego del despliegue desde una PC del dominio y complementarse con tickets/SPN; un 401 local no demuestra una falla de Kerberos en el diseño.
