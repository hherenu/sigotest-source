# Convenciones de UI — SIGO

Reglas que hoy están aplicadas en toda la capa Blazor (`src/Sigotest.Web/Components`).
Ante la duda, buscá un caso existente y copialo; si un cambio necesita salirse de una
regla, dejá el porqué en un comentario al lado (como hace el resto del código).

## Colores de botones (`ButtonStyle`)

| Rol | Estilo | Ejemplos |
| --- | ------ | -------- |
| Acción principal de la pantalla (**una sola por zona**) | `Primary` sólido | Calcular, Guardar, Nueva Obra, Importar N valores |
| Transición que **consolida** estado | `Success` sólido | Cerrar certificado, Aprobar, Marcar como Cargada, Tomar conocimiento |
| Transición que **revierte** | `Warning` flat | Reabrir, Volver a Cerrado, Enviar a revisión |
| Destructiva | `Danger` (text en filas, flat en cards) | Eliminar, Quitar bloque |
| Navegación, exportaciones y secundarias | `Light`/`Secondary` flat o text | Volver a X, Ver / Gestionar, Excel, Imprimir / PDF |

`Info` no se usa para acciones (quedó reservado al ícono "ver" de las filas).

## Íconos de acciones por fila

- Ver: `visibility` (Info, text) · Editar: `edit`/`edit_note` (Primary, text) ·
  Eliminar: `delete` (Danger, text). **`close` significa únicamente "Cancelar".**
- Botón deshabilitado: siempre con `title` que explique el motivo
  (ej. "Solo se puede eliminar el último disparo").

## Navegación

- Título de página siempre con `CabeceraPagina` (H1 + subtítulo + badges + acciones).
- Páginas anidadas bajo obra: parámetro `Migas` (último tramo = página actual, sin link).
- Botones de retorno: siempre "Volver a X" (nombran el destino).
- Nombres de módulo idénticos en menú, Home, H1 y `PageTitle`.

## Estados

- Carga: `<EstadoCargando Texto="Cargando X…" />` (nunca el spinner suelto).
- Vacío: `<EstadoVacio Icono=... Titulo=... Descripcion=...>` con CTA de alta si existe.
- Vacío por filtros en grillas: `EmptyText="Ningún registro coincide con los filtros aplicados."`.
- Estados de dominio → badge vía `EstadoUi` (mapa único). El color nunca es la única
  señal: acompañar con texto (ej. badge "Habilita / No habilita" junto a la variación).

## Formularios

- Obligatorios: `*` en el label del `RadzenFormField` + `RadzenRequiredValidator` con `Popup`.
- Ayudas largas: caption (`app-muted`) debajo del campo, no dentro del label flotante.
- Siglas de dominio: `title` en el FormField con el nombre completo.
- Ediciones cortas (≤5 campos): diálogo con `Width = "min(Npx, 92vw)"`; el llamador
  pasa una COPIA del VM y recarga si el diálogo cierra con `true`.

## Grillas y tablas

- `RadzenDataGrid`: `Density.Compact`, `PageSize="20"`, sin sombras.
- Grillas con carga masiva de datos tipeados: `GuardNavegacion` con predicado de
  cambios contra la DB (ver CertificadoCargar / PlanificacionEditar).
- Inputs dentro de celdas: siempre con `aria-label` que identifique la fila/columna.
- Tablas HTML propias (`cert-grid`): `caption` con clase `sr-only` y `scope` en los `th`;
  el encabezado es sticky (los detalles de fondo opaco y dos filas viven en
  `app-blazor.css`, sección cert-grid — no tocar sin leer los comentarios).

## Feedback y errores

- Toda mutación pasa por `Persistencia.EjecutarAsync` (traduce concurrencia/DB/reglas
  a notificaciones) y las confirmaciones por `UiAcciones` (flag `procesando` ANTES del
  Confirm: un doble clic no apila diálogos).
- Notificación de éxito corta (3-4s); advertencias con instrucción de recuperación.

## Varios

- Placeholders de dropdowns: `-- Seleccionar --` / `-- Seleccionar obra --`.
- Montos y cantidades: helpers de `Fmt` (cultura es-AR explícita), nunca `ToString` directo.
- CSS: solo `app-blazor.css`, derivado de tokens `--rz-*`; sin `<style>` en páginas;
  los `!important` existentes pisan internals de Radzen y están comentados — revisarlos
  en cada upgrade del paquete.
