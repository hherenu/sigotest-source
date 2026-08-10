# Autenticación Windows

La aplicación registra Negotiate y ejecuta `UseAuthentication()` antes de `UseAuthorization()`. Una fallback policy exige identidad autenticada para cualquier endpoint nuevo. `/Error` usa `AllowAnonymous` y `GET /health` declara acceso anónimo explícito.

No existen login, usuarios ni contraseñas locales. En IIS se deberá habilitar Windows Authentication y deshabilitar Anonymous Authentication para el sitio, conservando `/health` anónimo a nivel de ASP.NET Core según la política operativa acordada.

Negotiate es el mecanismo de negociación. Puede seleccionar Kerberos o NTLM; `User.Identity.AuthenticationType` no basta para demostrar Kerberos. La validación requiere consultar SPN y tickets desde un cliente de dominio, como describe `05-kerberos.md`.
