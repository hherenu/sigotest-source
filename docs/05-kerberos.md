# Kerberos

## Negotiate no equivale a Kerberos

Negotiate selecciona Kerberos cuando SPN, identidad, DNS, cliente y políticas lo permiten; en caso contrario puede usar NTLM. El tipo de autenticación visible no certifica el protocolo final.

Con la identidad virtual `ApplicationPoolIdentity`, el proceso usa la cuenta de equipo para acceso de red y normalmente el SPN HTTP corresponde a esa cuenta. Si el pool usa una cuenta de servicio, el SPN debe pertenecer a esa cuenta. Nunca debe duplicarse entre identidades.

## Validación

El script `ops/Test-Kerberos.ps1` consulta identidad, autenticación, `setspn -Q` y `klist` sin modificar nada. Desde una PC cliente del dominio: purgar tickets sólo si la política lo permite, acceder por `http://sigotest.sbase.com.ar`, ejecutar `klist` y confirmar un ticket `HTTP/sigotest.sbase.com.ar`.

El double hop aparece cuando la aplicación intenta delegar la identidad del usuario hacia otro servicio, por ejemplo SQL Server. No se configurará delegación salvo que un requisito real la justifique y seguridad apruebe el modelo.

No ejecutar `setspn -S`, `-A` ni `-D` durante diagnóstico.
