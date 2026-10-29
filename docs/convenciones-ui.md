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
- `Disabled` se reserva para el estado transitorio `procesando` (no necesita explicación).
  Cuando una **regla de negocio** impide la acción, el botón queda HABILITADO y el clic
  notifica el motivo (`Notify(Info, ...)`): un botón disabled no recibe foco ni hover
  táctil, y el motivo puesto en `title` era invisible por teclado y lector de pantalla.
  El `title` se conserva además como pista para mouse
  (ej. "Solo se puede eliminar el último disparo").

## Navegación

- Título de página siempre con `CabeceraPagina` (H1 + subtítulo + badges + acciones).
- Páginas anidadas bajo obra: parámetro `Migas` (último tramo = página actual, sin link).
- Botones de retorno: siempre "Volver a X" (nombran el destino).
- Nombres de módulo idénticos en menú, Home, H1 y `PageTitle`.

## Estados

- Carga: `<EstadoCargando Texto="Cargando X…" />` (nunca el spinner suelto).
  La colección de la página se declara **nullable** (`null` = cargando); inicializarla
  en `new()` hace que el estado vacío se muestre durante toda la carga (pasó en /usuarios).
- Vacío: `<EstadoVacio Icono=... Titulo=... Descripcion=...>` con CTA de alta si existe.
- Vacío por filtros en grillas: `EmptyText="@GridTextos.SinCoincidencias"` (en los listados,
  `GridTextos.SinCoincidenciasListado`, que nombra «Quitar filtros»).
- Estados de dominio → badge vía `EstadoUi` (mapa único). El color nunca es la única
  señal: acompañar con texto (ej. badge "Habilita / No habilita" junto a la variación).

## Formularios

- Obligatorios: `*` en el label del `RadzenFormField` + `RadzenRequiredValidator` con `Popup`.
- Límites numéricos de negocio (no negativos, mayores a cero, rangos de año): nunca
  `Min`/`Max` del `RadzenNumeric`, porque Radzen lleva el valor tipeado al límite sin avisar
  (un PU de -500 se guardaba como 0). Una sola regla por campo en el validador del módulo
  (`Services/Validaciones`), con su mensaje en una constante: el servicio la exige y el
  form la evalúa tal cual, sin repetir límites ni textos:
  `<RadzenCustomValidator Component="Peso" Validator="@(() => PonderacionValidator.Peso(form.Peso) is null)" Text="@PonderacionValidator.PesoFueraDeRango" Popup="true" />`.
  La regla decide qué hacer con el vacío (los campos opcionales lo dejan pasar). Sin
  formulario (pegado de índices), la aplica el análisis y la fila queda inválida con el
  mismo mensaje. `ConvencionesUiTests` falla si vuelve a aparecer un `Min`/`Max`.
- Textos que se guardan: `MaxLength` con el largo de su columna (`LargoTexto`, el mismo que
  configura `AppDbContext`), para que no se pueda tipear más de lo que entra en la base.
- Ayudas largas: caption (`app-muted`) debajo del campo, no dentro del label flotante.
- Siglas de dominio: `title` en el FormField con el nombre completo.
- Ediciones cortas (≤5 campos): diálogo con `Width = "min(Npx, 92vw)"`; el llamador
  pasa una COPIA del VM y recarga si el diálogo cierra con `true`.
- Formularios largos (ObraForm): `GuardNavegacion` con `Instantanea<T>` (foto JSON del modelo al
  cargar) y un flag de guardado que se pone dentro de la acción de guardar, antes de navegar.

## Grillas y tablas

- `RadzenDataGrid`: `Density.Compact`, `PageSize="20"`, sin sombras, `AllowColumnResize="true"`
  en todas. Además de dejar ajustar anchos, evita que Radzen borre filtros y orden cada vez que
  la grilla recibe otra `Data` (sin redimensionar, Radzen 10 resetea las columnas).
- El padding de celdas y títulos sale de los tokens `--rz-grid-cell-padding` y
  `--rz-grid-header-title-padding-inline` (`app-blazor.css`), con el mismo margen izquierdo:
  así el título queda alineado con sus datos. Nada de `padding … !important` en `td`.
- Las grillas del layout (listados) tienen piso de alto (`min-height: 20rem`): si no entran, scrollea
  la página. Sin piso, en una notebook con zoom mostraban 0 o 1 fila.
- Listados: **todas las columnas con `Width`** (en px, como mínimo de la columna). Con
  `table-layout: fixed` una columna sin ancho recibe solo el sobrante y queda en «…»; con todas
  declaradas la tabla scrollea a lo ancho y, en pantallas anchas, reparte el sobrante. `MinWidth`
  no sirve para esto: Radzen lo emite como `min-width` en la celda y la tabla fija lo ignora.
  Columna de acciones: 28 px por ícono + separación + 32 de padding (3 íconos ≈ 130px).
- Grillas con filtro: `ApplyFilterText="@GridTextos.AplicarFiltro"` y
  `ClearFilterText="@GridTextos.LimpiarFiltro"` (los de Radzen están en inglés).
- Listados (grilla con filtro y paginado; portado de SAF): `<GrillaListado Vista="@vista">` con
  las columnas adentro y `<FiltrosGrilla Vista="@vista" Placeholder="Buscar …" />` en las acciones
  de la cabecera. La configuración de la grilla, el pie («N de M filas») y el estado viven en el
  componente; la página solo arma la `VistaGrilla<T>` sobre `EstadoGrillas.De("ruta")` (por obra o
  índice si el listado cuelga de uno), con los campos de la búsqueda, y carga con
  `vista.Cargar(lista)`. Filtros, orden, anchos, página y búsqueda sobreviven a la navegación
  dentro de la pestaña (lo que elige la página fuera de la grilla, como el corte de
  Planificación, no). Si una página cambia valores de las filas en el lugar, llama a
  `vista.RefrescarOpcionesDeFiltro()`: Radzen guarda las opciones de cada filtro de lista y,
  con `AllowColumnResize`, otra `Data` no las invalida (la vista lo hace sola al cambiar las filas).
- Grillas con filtro: todas las columnas filtran salvo la de acciones (título vacío o «Acciones»).
  Las calculadas muestran una propiedad de texto del VM (`EstadoPlanTexto`, `CicloDelMes`,
  `RolesTexto`, `ActivoTexto`…), que es lo que ofrece su filtro; si el texto no ordena bien,
  `SortProperty` con una clave. **Nunca `FilterProperty`**: en el filtro de lista, si difiere de
  `Property`, Radzen toma `Property` como colección y abrir el filtro rompe la página. Las opciones
  se ordenan alfabéticamente: Período filtra por año (`Property="Anio"`, `SortProperty="PeriodoOrden"`,
  el texto en el `Template`) y el mes se encuentra con la búsqueda rápida.
- `ConvencionesUiTests` verifica: redimensionables en todas, textos compartidos en las que
  filtran, listados con `GrillaListado` + `FiltrosGrilla` y todas sus columnas con ancho, columnas
  filtrables sin `FilterProperty`, piso de alto y títulos alineados en el CSS, y tablas sin filas
  centradas ni sangría armada a mano.
- Grillas con carga masiva de datos tipeados (CertificadoCargar / PlanificacionEditar):
  `GuardNavegacion` con predicado de cambios contra la DB y contador «N cambios sin guardar»
  junto a cada «Guardar». **Guardar no saca de la página** (se guarda de a poco) y después de
  guardar se relee; si el guardado falla, se relee **conservando lo tipeado** (el token queda al
  día) y el conflicto se avisa con un mensaje propio (`Persistencia.EjecutarAsync(…, conflicto:)`):
  el genérico pide recargar la página, que borra lo conservado.
- Una acción que se aplica al momento dentro de una pantalla que espera «Guardar» (ej. «Quitar
  bloque»): el confirm dice que es inmediata, qué borra y que no se puede deshacer.
- Inputs dentro de celdas: siempre con `aria-label` que identifique la fila/columna. Los
  numéricos de una columna de cifras llevan `TextAlign="TextAlign.Right"` (al editar, el número
  no salta de lado); dentro de `cert-grid` toman el alto y la letra de la fila.
- Alineación: texto a la izquierda; **cifras a la derecha, título incluido**, para que los
  decimales queden encolumnados; centrado solo para N°, unidad, fechas, badges e íconos. En
  `cert-grid`: clase `num` en el `th` y los `td` de cifras, `centro` en los centrados, y los
  títulos de grupo (`colspan`) van centrados solos. Nunca una fila entera centrada.
- Tablas jerárquicas (rubros e ítems de una estructura): la sangría va en la **Descripción**,
  para rubros e ítems por igual, con `FilaEstructuraUi.Sangria(nivel)` (16 px por nivel); el
  Código va sin sangría, como columna alineada.
- Tablas HTML propias (`cert-grid`): `caption` con clase `sr-only` y `scope` en los `th`;
  el encabezado es sticky (los detalles de fondo opaco y dos filas viven en
  `app-blazor.css`, sección cert-grid — no tocar sin leer los comentarios).
- `cert-grid` con descripciones largas (carga y detalle del certificado): clase
  `cert-grid-ajustable`, `cert-col-desc` en el th y los td de la descripción (con `title`
  del texto completo) y `<AsaAnchoDescripcion />` dentro de ese th. La descripción ocupa el
  ancho libre y el usuario la ajusta arrastrando el asa; el script vive en `App.razor`.

## Feedback y errores

- Toda mutación pasa por `Persistencia.EjecutarAsync` (traduce concurrencia/DB/reglas
  a notificaciones) y las confirmaciones por `UiAcciones` (flag `procesando` ANTES del
  Confirm: un doble clic no apila diálogos). Lo mismo al abrir un diálogo con
  `DialogService.OpenAsync`: `procesando = true` antes de abrirlo y `false` en un `finally`.
- Notificación de éxito corta (3-4s); advertencias con instrucción de recuperación.
- Conexión: el aviso de reconexión es propio y en castellano (`#components-reconnect-modal` en
  `App.razor`, estilos por estado en `app-blazor.css`); dice que lo no guardado sigue en pantalla
  mientras no se recargue. `#blazor-error-ui` (MainLayout) es para errores no controlados, no para
  cortes de conexión.

## Varios

- Placeholders de dropdowns: `-- Seleccionar --` / `-- Seleccionar obra --`.
- Montos y cantidades: helpers de `Fmt` (cultura es-AR explícita), nunca `ToString` directo.
  Montos con 2 decimales (`Fmt.N2`/`Fmt.Money`); cantidades y precios unitarios con
  `Fmt.HastaN4` (2 como mínimo, hasta los 4 que guarda la base) en pantalla y
  `ExcelHelpers.FormatoCantidad` en los Excel, para que cantidad × PU cuadre con el monto.
- CSS: solo `app-blazor.css`, derivado de tokens `--rz-*`; sin `<style>` en páginas;
  los `!important` existentes pisan internals de Radzen y están comentados — revisarlos
  en cada upgrade del paquete.
