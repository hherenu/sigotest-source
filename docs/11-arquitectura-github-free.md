# Arquitectura GitHub Free

## Repositorio de desarrollo

`sigotest-desarrollo` se comparte con el desarrollador. Contiene código y build CI, pero ningún self-hosted runner, secreto o capacidad de despliegue. El trabajo entra por ramas y Pull Requests revisados.

## Repositorio de despliegue

`sigotest-deploy` es exclusivo del administrador. Recibe código aprobado mediante importación local controlada, aloja el workflow de despliegue y se asocia al runner. Un push autorizado a `main` o despacho manual inicia la entrega.

Esta separación evita ejecutar sobre el servidor código arbitrario de una rama o PR del desarrollador. Es compatible con GitHub Free y no depende de reglas pagas; la revisión y promoción son controles administrativos explícitos.
