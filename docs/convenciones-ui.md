# Convenciones de UI — SIGO

Reglas de la capa Blazor (`src/Sigotest.Web/Components`). Cada regla dice qué hacer y, en una
línea, por qué.

- **[T]**: la verifica `tests/SIGO.Tests/ConvencionesUiTests.cs` (el anexo dice qué test). Sin
  marca: se revisa a mano.
- **Excepción**: si un caso necesita salirse de una regla, dejá el porqué en un comentario al lado.
- **Código viejo**: si encontrás algo que no cumple una regla, corregilo al tocarlo. Deudas
  conocidas: números con `ToString("N2")` en Redeterminación (ver «Formato de datos»), «Volver a
  Cerrado» en el certificado, `Data="@….OrderBy(…)"` en la ponderación y el mensaje técnico del
  `ErrorBoundary`.
- **Ante la duda**, copiá un caso existente que cumpla la regla.

## 1. Página

### Cabecera

- **[T] Toda página con ruta usa `CabeceraPagina`**, que pone el único H1. Excepciones: el Home
  (portada) y `SinAcceso` (pantalla centrada).
- **[T] Foco al navegar**: `FocusOnNavigate` (en `Routes.razor`) enfoca el H1 para que el lector de
  pantalla anuncie la página. Va sin recuadro (`h1:focus` en `app-blazor.css`): no es un control y
  el navegador lo marcaba al cargar o recargar. No saques `FocusOnNavigate` para evitar el marco.
- **[T] Nunca `href="#ancla"` suelto**: con `<base href="/">` se resuelve contra la portada
  («/#ancla») y navega al inicio. Ruta actual + ancla, como el «Saltar al contenido» de
  `MainLayout`, que además mueve el foco al contenido (el scroll de Blazor al ancla no lo mueve).
- **[T] H4 solo en las páginas del menú** (Obras, Índices INDEC, Calcular, Historial, Planificación,
  Usuarios: `Estilo="TextStyle.H4"`). El resto queda en H5, el default (no se escribe). *Por qué:* el
  título saltaba de tamaño entre páginas hermanas.
- Parámetros: `Titulo`, `Subtitulo` (caption), `SubtituloExtra` (contenido libre bajo el título),
  `Badges`, `Acciones` (botones a la derecha) y `Migas`. `Clase` trae el margen de 1rem al contenido:
  no lo cambies.
- **Migas** (`Migas`) en toda página de tercer nivel (bajo una obra: certificados, estructuras,
  carga…); en el segundo nivel son opcionales. El último tramo es la página actual, sin link.
- **Retorno**: «Volver a X», nombrando el destino. «Volver a…» es solo navegación, nunca una
  transición de estado.
- **[T] Las secciones de una obra** (estructuras, certificados, ponderación) **vuelven a donde se
  entró**: a la ficha («Volver a Obra») o, desde las columnas de acceso, al listado («Volver a
  Obras»). Lo resuelve `RetornoObra`: el listado lo marca, la ficha lo desmarca y las pantallas
  internas no lo tocan. Un F5 lo olvida y vuelve a la ficha.
- **Nombres**: el del módulo es el mismo en menú, Home, H1 y `PageTitle`. Los subítems de un grupo
  del menú se abrevian (Redeterminación › «Calcular» es la página «Calcular Redeterminación»).

### Títulos dentro de la página

| Qué | Cómo |
| --- | --- |
| Título de una tarjeta o de un paso | **[T]** `TextStyle.H6` + `TagName="TagName.H2"` (sin el nivel quedaban `<h6>` colgando del `<h1>`) |
| Título de un bloque o sección sin tarjeta (bloque del certificado, obra del historial, tabla de ponderación) | `TextStyle.Subtitle1` + clase `app-titulo-bloque` |
| Total destacado junto a un título (Monto Total Contrato, Σ Pesos) | `TextStyle.Subtitle2` + clase `app-total` |
| Dato destacado dentro de un texto | clase `app-destacado` |
| Antetítulo («Resultado» del banner) | `TextStyle.Overline`, nunca como título |

- **[T] Sin contadores junto al título** (badge con la cantidad, como «Prórrogas de plazo 0»): la
  lista está a la vista y, si está vacía, lo dice su texto («Sin prórrogas…»).

### Estados

- **Cargando**: `<EstadoCargando Texto="Cargando obras…" />` (o la acción en curso: «Leyendo la
  planilla…»), nunca el spinner suelto. La colección de la página es **nullable** (`null` =
  cargando): inicializada en `new()`, el estado vacío se mostraba durante toda la carga.
- **Vacío**: `<EstadoVacio Icono=… Titulo=… Descripcion=…>`, con el botón de alta si existe.
  Excepción: Índices muestra la grilla vacía (es un catálogo que se completa importando).
- **Sin coincidencias** en una grilla con filtro: `EmptyText="@GridTextos.SinCoincidencias"`. En
  los listados lo pone `GrillaListado` sola.
- **No encontrado** (el registro no existe o lo borró otro usuario): `RadzenAlert` Warning.
- **Alertas en la página**: `RadzenAlert Variant="Variant.Flat" Shade="Shade.Lighter" AllowClose="false"`.
- **Estados de dominio**: en una grilla o tabla, **texto** (sección 5, «Celdas en texto»). El badge
  con el estilo de `EstadoUi` (un solo mapa) queda para el estado del registro que se está viendo,
  uno por página: cabecera, ficha o diálogo. **El color nunca es la única señal**: va con texto (el
  badge dice el nombre del estado). Excepción decidida: la «Variación Ref. %» del Historial va solo
  en color (verde si habilita, rojo si no), con el criterio en el `title` de la celda y del
  encabezado.
- **Texto secundario**: clase `app-muted`.

## 2. Escala

Una sola escala para que las páginas hermanas se vean iguales.

| Qué | Regla | Cómo |
| --- | --- | --- |
| Texto | **[T]** tamaños de Radzen (Body1, Body2, Caption) | `app-fs-body2` / `app-fs-caption` para textos sueltos; nada de `font-size` inline (salvo `RadzenIcon`) ni `font-weight` inline en un `RadzenText`. Excepción: tablas `cert-grid` (.8rem) |
| Separaciones | **[T]** múltiplos de 4 px | `Gap` en {0, 0.25rem, 0.5rem, 0.75rem, 1rem, 1.5rem}; `margin`/`padding` inline y del CSS en cuartos de rem |
| Ritmo vertical | cabecera → contenido y entre tarjetas: 1rem; título de tarjeta → contenido: 0.75rem | default de `CabeceraPagina`; `rz-mb-4` entre tarjetas; `rz-mb-3` bajo el título |
| Gap interno | formularios 1rem; diálogos 0.75rem; barras de botones 0.5rem | `Gap="…"` del `RadzenStack` / `RadzenRow` |
| Ancho de formulario | **[T]** tres medidas | `app-form-angosto` (560px), `app-form-medio` (720px), `app-form-ancho` (960px) |
| Ancho de diálogo | **[T]** tres medidas | `AnchoDialogo.Corto` (hasta ~5 campos), `Medio` (dos columnas o ayudas largas), `Ancho` (tablas de revisión) |
| Ancho de columna | **[T]** por tipo de dato | `AnchoColumna` (ver «Columnas») |
| Largo de línea | descripciones y ayudas | `app-medida-corta` (48ch) |

## 3. Botones

### Color, por rol

| Rol | Estilo | Ejemplos |
| --- | --- | --- |
| Acción principal de su zona (barra, tarjeta, diálogo); **una por zona** — repetirla arriba y abajo de una pantalla larga cuenta como una | `Primary` sólido | Guardar, Calcular, Nueva Obra, Importar publicación |
| Transición que **consolida** un estado | `Success` sólido | Cerrar, Aprobar, Marcar como Cargada, Tomar conocimiento |
| Transición que **revierte** | `Warning` flat (sólido solo si envía su propio formulario, como «Enviar a revisión» con el motivo) | Reabrir, Enviar a revisión |
| Destructiva | `Danger`: text en filas y cabeceras de bloque, flat en tarjetas | Eliminar, Quitar bloque |
| Navegación, exportación y secundarias | `Light` o `Secondary`, flat o text | Volver a X, Ver / Gestionar, Exportar Excel, Imprimir / PDF |

`Info` no se usa para acciones: quedó para el ícono «ver» de las filas.

### Tamaño, por lugar

| Lugar | Tamaño |
| --- | --- |
| Barra de acciones: cabecera de página (`<Acciones>`), cabecera de tarjeta, banner | **[T]** `ButtonSize.Small` |
| Ícono de acción en una fila | `ButtonSize.Small` |
| Botón con texto dentro de una celda (año plegable, «Excel» de una versión) | `ButtonSize.ExtraSmall` |
| Envío al pie de un formulario de página o de un diálogo (Guardar / Cancelar) | Medium (sin `Size`) |

**Una barra, un tamaño.** En la cabecera, buscadores y selectores toman la altura de los botones
(clase `app-cabecera-acciones`, que `CabeceraPagina` ya pone).

### Íconos de las filas

| Acción | Ícono | Estilo |
| --- | --- | --- |
| Ver | `visibility` | Info, text |
| Editar | `edit` / `edit_note` | Primary, text |
| Eliminar | `delete` | Danger, text |
| Descargar Excel | `download` | Secondary, text |
| Recalcular | `calculate` | Secondary, text |
| Ir a una sección de la obra: estructuras, certificados, ponderación | `arrow_forward` en las tres (la columna dice cuál; par del `arrow_back` de «Volver») | Primary, text (como Editar) |

- **Columnas de acceso** (Obras): una por sección, con el nombre de la sección como título, un
  botón de ícono y `Width="120px"`, sin orden ni filtro. Van en el orden de la ficha de la obra.

- **Todo botón de solo ícono lleva `title`**: es su nombre para el lector de pantalla y la pista
  para el mouse.
- `close` significa **únicamente «Cancelar»**.

### Habilitado o deshabilitado

- `Disabled` solo para:
  - el estado transitorio `procesando` (no necesita explicación);
  - «no hay nada que hacer» con el motivo a la vista (Importar con 0 filas válidas: el resumen de
    la pantalla dice por qué).
- Si lo impide una **regla de negocio** que no está a la vista, el botón queda **habilitado** y el
  clic avisa el motivo (`Notify(Info, …)`); el `title` lo anticipa para el mouse. *Por qué:* un
  botón deshabilitado no recibe foco y su `title` no lo oye un lector de pantalla.
- El botón que guarda lleva `IsBusy="@procesando" BusyText="Guardando..."`.

### Permisos

- Página: `@attribute [Authorize(Roles = Roles.X)]`.
- Botones que dependen del rol: `<AuthorizeView Roles="@Roles.X">`.
- El servicio vuelve a exigir el rol (`ExigirRol`): la UI no es la barrera.

## 4. Formularios y diálogos

- **Obligatorios**: `*` en el texto del `RadzenFormField` + `RadzenRequiredValidator` con `Popup`.
- **[T] Límites numéricos** (no negativos, rangos de año): nunca `Min`/`Max` en `RadzenNumeric`,
  que lleva el valor al límite sin avisar (un PU de -500 se guardaba como 0). La regla vive en el
  validador del módulo (`Services/Validaciones`), con su mensaje en una constante; el servicio la
  exige y el formulario la evalúa igual:

  ```razor
  <RadzenCustomValidator Component="Peso" Validator="@(() => PonderacionValidator.Peso(form.Peso) is null)"
                         Text="@PonderacionValidator.PesoFueraDeRango" Popup="true" />
  ```

- **[T] Textos que se guardan**: `MaxLength` con el largo de su columna (`LargoTexto`, el mismo de
  `AppDbContext`).
- **Ayudas largas**: caption `app-muted` debajo del campo, no dentro del label flotante.
- **Siglas**: `title` en el `RadzenFormField` con el nombre completo.
- **Placeholders de selectores**:
  - elegir uno obligatorio: `-- Seleccionar X --`;
  - filtro: `-- Todas las X --`;
  - el vacío es un valor válido: «(ninguno)», «Sin mes».
- **Botonera**: Guardar (Primary, Medium, con `IsBusy`) y Cancelar (Light flat, ícono `close`).
  En un diálogo va al pie, `JustifyContent.End`, Cancelar primero.
- **Diálogos**:
  - ancho con `AnchoDialogo` **[T]**;
  - quien abre pone su flag (`procesando`) **antes** de `OpenAsync` y lo baja en un `finally`, así
    un doble clic no abre dos;
  - el diálogo cierra con `DialogService.Close(true)` si cambió algo, o con su resultado; quien lo
    abrió recarga.
- **Cambios sin guardar**: `<GuardNavegacion HayCambios="@HayCambiosSinGuardar" />` pide
  confirmación al salir.
  - En un formulario largo, el predicado sale de una `Instantanea<T>`, una foto JSON del modelo:

    ```csharp
    private readonly Instantanea<ObraVM> instantanea = new();
    private bool guardado;
    private bool HayCambiosSinGuardar() => !guardado && instantanea.Cambio(obra);
    // al cargar: instantanea.Tomar(obra);
    // dentro de la acción de guardar, después del servicio y antes de navegar: guardado = true;
    ```

  - En una carga masiva, el predicado compara lo tipeado con lo guardado (sección 7).

## 5. Grillas Radzen

### Listados (grilla con filtro y paginado)

- **[T] Un listado es `GrillaListado` + `FiltrosGrilla` sobre una `VistaGrilla<T>`.**
  - La configuración, el pie («N de M filas») y el estado (filtros, orden, anchos, página y
    búsqueda, que sobreviven a la navegación) viven en el componente.
  - La página declara las columnas y los campos de la búsqueda:

  ```razor
  @inject EstadoGrillas Grillas
  <CabeceraPagina Titulo="Obras" Estilo="TextStyle.H4">
      <Acciones>
          @if (obras is { Count: > 0 })
          {
              <FiltrosGrilla Vista="@vista" Placeholder="Buscar licitación, obra o contratista…" />
          }
          …
      </Acciones>
  </CabeceraPagina>
  <GrillaListado Vista="@vista">
      <RadzenDataGridColumn TItem="ObraVM" Property="Nombre" Title="Nombre" Width="240px" />
      …
  </GrillaListado>

  @code {
      private List<ObraVM>? obras;
      private VistaGrilla<ObraVM> vista = null!;
      protected override async Task OnInitializedAsync()
      {
          vista = new(Grillas.De("obras"), o => [o.NumeroLicitacion, o.Nombre, o.Contratista]);
          obras = await ObraService.ListarAsync();
          vista.Cargar(obras);
      }
  }
  ```

- La clave de `Grillas.De` es la ruta. Si el listado cuelga de una obra o de un índice, va con su
  id (`$"obras/{ObraId}/certificados"`), y la vista se arma en `OnParametersSet`.
- El `Placeholder` nombra los campos que recorre la búsqueda.
- Para exportar lo que se ve: `vista.Grid.View` (respeta búsqueda y filtros).
- Si la página cambia valores de las filas **en el lugar**, llama a
  `vista.RefrescarOpcionesDeFiltro()`. Al cambiar las filas con `vista.Cargar`, la vista lo hace sola.
- Lo que la página elige fuera de la grilla (el corte de Planificación) no se recuerda.

### Otras grillas (detalle, edición, vista previa)

- `Density="Density.Compact"` y **[T]** `AllowColumnResize="true"`. *Por qué:* sin
  redimensionar, Radzen 10 borra filtros y orden cada vez que la grilla recibe otra `Data`.
- **[T]** Con filtro: `ApplyFilterText="@GridTextos.AplicarFiltro"`,
  `ClearFilterText="@GridTextos.LimpiarFiltro"` y `EmptyText="@GridTextos.SinCoincidencias"`. Los
  textos de Radzen están en inglés.
- Paginado: las planillas de trabajo (carga, ponderación, ítems) no paginan; las vistas previas,
  con el tamaño que convenga.
- `Data` estable: una lista armada en el atributo (`Data="@x.OrderBy(…)"`) es otra en cada render,
  y Radzen vuelve a la página 1.

### Columnas

- **Ancho**:
  - **[T]** en los listados, **todas** las columnas declaran `Width`;
  - **[T]** las de tipo de dato estándar usan **su** constante de `AnchoColumna`:

    | Dato | Constante |
    | --- | --- |
    | N°, fila, línea, orden en una grilla que no ordena ni filtra | `Numero` (60px) |
    | Lo mismo en una grilla que ordena o filtra (todo listado) | `NumeroOrdenable` (100px) |
    | Fechas | `Fecha` |
    | Período | `Periodo` |
    | Código de ítem | `Codigo` |
    | Código de índice | `CodigoIndice` |
    | Unidad | `Unidad` |
    | Cantidades y valores de índice | `Cantidad` |
    | Montos y PU | `Importe` |
    | Porcentajes | `Porcentaje` |
    | Estado (también «Activo») | `Estado` |
    | Columna de n íconos | `Acciones(n)` |

  - las constantes ya contemplan los íconos de orden y filtro, salvo `Numero`: los íconos de un
    título centrado ocupan ~56px y con menos ancho el encabezado los recorta (no se ven);
  - las de texto libre (nombre, descripción, observaciones) declaran su ancho en px, como mínimo;
  - las congeladas declaran el suyo.

  *Por qué:* con `table-layout: fixed`, una columna sin ancho recibe solo el sobrante y queda en
  «…». Declaradas todas, la tabla se desplaza a lo ancho. `MinWidth` no sirve para esto: la tabla
  fija lo ignora.
- **Filtro**:
  - **[T]** con filtro, toda columna filtra salvo las de botones: la de acciones (título vacío o
    «Acciones») y las de acceso (sin `Property`, con un botón). Las demás filtran por `Property`;
  - **[T] nunca `FilterProperty`**: si difiere de `Property`, Radzen toma `Property` como
    colección y abrir el filtro rompe la página;
  - una columna calculada muestra una propiedad de texto del VM (`EstadoPlanTexto`, `RolesTexto`…),
    que es lo que ofrece su filtro;
  - si el texto no ordena bien, `SortProperty` con una clave;
  - Radzen ordena las opciones alfabéticamente: el Período filtra por año (`Property="Anio"`,
    `SortProperty="PeriodoOrden"`, el texto en el `Template`) y el mes se busca con la búsqueda
    rápida.
- **Alineación**:
  - texto a la izquierda;
  - **cifras a la derecha** (`TextAlign.Right`), para que los decimales queden encolumnados;
  - centrado solo para N°, unidad, fechas e íconos (los estados son texto: a la izquierda);
  - fechas con `FormatString="{0:dd/MM/yyyy}"`.
- **[T] Celdas en texto** (grillas y tablas `cert-grid`): estado, rol, tipo o conteo se escriben.
  - Sin `RadzenBadge`, sin fondo y sin íconos en color (`IconColor`) en una celda. *Por qué:* un
    badge por fila pone fondo y color en cada renglón; es ruido y compite con los datos.
  - Lo secundario o vacío («Sin roles», «—», «Sin versión») va con `app-muted`.
  - Color de texto, nunca fondo, solo para lo que hay que **corregir antes de importar o guardar**:
    `app-texto-error` (no se importa / no se aplica) y `app-texto-aviso` (advertencia). Así quedan
    las vistas previas de importación y el resumen de la planificación. Lo que está bien va sin
    color.
  - **[T]** En los listados (`GrillaListado`), ningún color de texto.
  - Excepción decidida: la «Variación Ref. %» y los totales del cálculo de la redeterminación, en
    verde si habilita y rojo si no (sección 1).
  - Fuera de la regla, porque no marcan un estado: el fondo de las filas de rubro y de subtotal, y
    el de la celda modificada en la planificación.
- **[T]** El padding de celdas y títulos sale de tokens de `app-blazor.css`, con el mismo margen
  izquierdo (título alineado con sus datos); nada de `padding … !important` en `td`.
- **[T]** Las grillas de los listados tienen piso de alto (`min-height: 20rem`): si no entran,
  scrollea la página. Sin piso, con zoom mostraban 0 o 1 fila.
- **Inputs dentro de celdas**: con `aria-label` que nombre la fila y la columna; los numéricos de
  una columna de cifras, con `TextAlign="TextAlign.Right"`.

## 6. Tablas `cert-grid` (HTML propio: certificado, ponderación, historial, totales)

- Dentro de `<div class="cert-scroll">`, que da el encabezado fijo y el scroll.
- `caption` con clase `sr-only` y `scope` en los `th`.
- **[T]** Celdas en texto, como en las grillas (sección 5).
- **[T] Nunca una fila entera centrada.** Las alineaciones son por celda:
  - `num` en el `th` y los `td` de cifras;
  - `centro` en los centrados;
  - los títulos de grupo (`colspan`) se centran solos.
- Clases:
  - `col-warning` / `col-primary` / `col-info` para los grupos «Anterior», «Mes» y «Acumulado»;
  - `agrup-0` / `agrup-1` para rubro y subrubro;
  - `cell-mono` para los códigos.
- **[T] Tablas jerárquicas** (rubros e ítems): la sangría va en la **Descripción**, para rubros e
  ítems por igual, con `FilaEstructuraUi.Sangria(nivel)`. El Código va sin sangría. En una
  `RadzenDataGrid`, el fondo del agrupador sale de `FilaEstructuraUi.EstiloAgrupador` en el
  `RowRender`.
- **Descripciones largas** (carga y detalle del certificado):
  - clase `cert-grid-ajustable`;
  - `cert-col-desc` en el `th` y en los `td` de la descripción, con `title` del texto completo;
  - `<AsaAnchoDescripcion />` dentro de ese `th` (el script vive en `App.razor`).
- Letra de .8rem: la única excepción a la escala. Los campos dentro de la tabla toman el alto y la
  letra de la fila.
- Los detalles del encabezado fijo están en `app-blazor.css`, sección cert-grid: no los toques sin
  leer los comentarios.

## 7. Carga masiva (certificado, planificación)

- `GuardNavegacion` con un predicado que compara lo tipeado con lo guardado, y el contador
  «N cambios sin guardar» junto a cada «Guardar».
- **Guardar no saca de la página** (se guarda de a poco) y después se relee lo guardado.
- Si el guardado falla, se relee **conservando lo tipeado**, con el token al día. El conflicto se
  avisa con mensaje propio: `Persistencia.EjecutarAsync(…, conflicto: "…")`. El mensaje genérico
  pide recargar la página, y eso borra lo conservado.
- Una acción que se aplica al momento en una pantalla que espera «Guardar» (Quitar bloque): el
  confirm dice que es inmediata, qué borra y que no se puede deshacer.

## 8. Feedback y errores

| Situación | Cómo |
| --- | --- |
| Cualquier escritura | `Persistencia.EjecutarAsync(acción, Notifications)`: traduce concurrencia, base y reglas de negocio en avisos. Las reglas previas se tiran como `InvalidOperationException` dentro de la acción |
| Confirmar y ejecutar | `UiAcciones.ConfirmarYEjecutarAsync` |
| Confirmar, eliminar y recargar | `UiAcciones.ConfirmarEliminarYRecargarAsync` (con el `RowVersion` de la fila; «ya eliminado» se avisa aparte) |
| Guardar un formulario y navegar | `UiAcciones.GuardarYNavegarAsync` |

- El flag (`procesando`) se pone **antes** del Confirm o del `OpenAsync`, y se baja en un
  `finally`: un doble clic no apila diálogos.
- **Duración de los avisos**:

  | Tipo | Duración |
  | --- | --- |
  | Éxito | 3-4 s; 5-6 s si trae una instrucción |
  | Advertencia | 6-10 s, con la instrucción para salir del problema |
  | Error | 6-8 s |

- **Conexión**: el aviso de reconexión es propio y en castellano (`#components-reconnect-modal` en
  `App.razor`, estilos por estado en el CSS) y dice qué pasa con lo no guardado. **[T]**
- **Error no controlado**: `#blazor-error-ui` (MainLayout) aparece cuando la página dejó de
  responder. No es el aviso de conexión. Un error de una página lo contiene el `ErrorBoundary` del
  layout.

## 9. Formato de datos

| Dato | Helper |
| --- | --- |
| Montos | `Fmt.Money` («$ 1.234,56», vacío si es 0) o `Fmt.N2` |
| Cantidades y precios unitarios | `Fmt.HastaN4` (2 a 4 decimales) y `Fmt.Qty` (vacío si es 0) |
| Porcentajes | `Fmt.N2(p) + "%"` (4 decimales: `Fmt.N4`) |
| Meses | `Fmt.MesNombre`, `MesCorto` y `MesNombreSeguro` (datos viejos) |
| Publicaciones INDEC | `Fmt.PubRevista`, `PubLabelRevista` y `PubMayus` |
| Moneda | `Fmt.Simbolo` |
| Fechas | `dd/MM/yyyy` (en grilla, `FormatString="{0:dd/MM/yyyy}"`) |
| Excel | `ExcelHelpers.FormatoCantidad`, para que cantidad × PU cuadre con el monto |

**Nunca `ToString("N2")` directo**: `Fmt` fija la cultura es-AR y `ToString` depende de la del
servidor.

## 10. CSS

- Solo en `wwwroot/css/app-blazor.css`, derivado de tokens `--rz-*` para que funcione en claro y en
  oscuro. Sin `<style>` en las páginas.
- **[T] Sin colores fijos por nombre** (`gray`, `black`, `white`…): un neutro se mezcla del texto
  del tema, `color-mix(in srgb, var(--rz-text-color) N%, transparent)`.
- **Tema por defecto: claro** (`material`). El oscuro queda solo si el usuario lo eligió con el
  botón claro/oscuro (cookie `SIGOTheme`, la normaliza `Temas.Normalizar`).
- **`style` inline permitido**:
  - `width:100%` en campos;
  - colores dinámicos con `var(--rz-…)`;
  - ajustes puntuales de flex o `display`.
- **[T] `style` inline prohibido**:
  - `font-size` (salvo íconos);
  - `font-weight` en un `RadzenText`;
  - `max-width` (usá `app-form-*` o `app-medida-corta`);
  - separaciones fuera de los múltiplos de 4 px.
- **Clases propias**:
  - **Texto**: `app-muted`, `app-titulo-bloque`, `app-total`, `app-destacado`, `app-fs-body2`,
    `app-fs-caption`, `app-mono` (códigos en grillas);
  - **Anchos**: `app-form-angosto` / `-medio` / `-ancho`, `app-medida-corta`;
  - **Grillas y tablas**: `app-grid-wrap` (vistas previas con texto largo), `cert-grid` y sus clases
    (sección 6);
  - **Accesibilidad**: `sr-only` (solo para lectores de pantalla);
  - **Impresión**: `d-print-none` y `#print-section`.
- `!important` solo para pisar internals de Radzen, con un comentario que diga cuál y por qué.
  Revisalos en cada actualización del paquete.

## 11. Componentes y helpers compartidos (`Components/Shared`)

| Pieza | Para qué |
| --- | --- |
| `CabeceraPagina` | Título, subtítulo, badges, migas y acciones de toda página |
| `EstadoCargando` / `EstadoVacio` | Carga y lista vacía |
| `GrillaListado` / `FiltrosGrilla` / `VistaGrilla` / `EstadoGrillas` / `GridTextos` (`Grillas.cs`) | Listados (sección 5) |
| `GuardNavegacion` | Confirmar al salir con cambios sin guardar |
| `BannerResultado` / `TablaPonderacionCalculo` | Resultado y tabla de una redeterminación (Calcular y cálculo guardado) |
| `ContrasteTotales` | Total calculado contra el de la planilla, al importar |
| `SelectorPlanilla` | Elegir un archivo Excel (con teclado) |
| `AsaAnchoDescripcion` | Ancho ajustable de la descripción en `cert-grid` |
| `PaginaError` | Página de error estática, sin circuito |
| `Persistencia`, `UiAcciones` | Escrituras y confirmaciones (sección 8) |
| `Fmt`, `Redet` | Formato (sección 9) y umbral de la redeterminación (`EsSuba`, `ColorSuba`) |
| `EstadoUi` | Estilo de badge por estado de dominio (fuera de las grillas) y texto del estado de la planificación |
| `FilaEstructuraUi`, `Arbol` | Sangría y fondo de rubros; aplanar el árbol de ítems |
| `AnchoColumna`, `AnchoDialogo` | Anchos de la escala |
| `Instantanea<T>` | Detectar cambios en un formulario |
| `RetornoObra` | A dónde vuelven las secciones de una obra: ficha o listado (sección 1) |

## Anexo: qué verifica cada test de `ConvencionesUiTests`

| Test | Regla |
| --- | --- |
| `RadzenNumeric_SinMinNiMax` | Sin `Min`/`Max` en numéricos |
| `TextosQueSeGuardan_ConMaxLength` | `MaxLength` en los textos que se guardan |
| `RadzenDataGrid_ConColumnasRedimensionables` | `AllowColumnResize="true"` en toda grilla |
| `GrillasConFiltro_ConLosTextosCompartidos` | Textos de `GridTextos` en las grillas con filtro |
| `GrillasConFiltro_TodasLasColumnasFiltrables` | Columnas filtrables por `Property`, sin `FilterProperty` (salvo las de botones) |
| `Titulos_SinContadores` | Sin badge con la cantidad junto a un título |
| `SeccionesDeLaObra_VuelvenADondeSeEntro` | Accesos Primary que marcan `RetornoObra`; secciones con su «Volver» |
| `Listados_ConGrillaListadoYBusqueda` | Los listados son `GrillaListado` + `FiltrosGrilla` |
| `Listados_TodasLasColumnasConAncho` | Toda columna de un listado con `Width` |
| `Escala_ColumnasDeTiposEstandarConSuAnchoColumna` | Cada tipo de dato con su constante de `AnchoColumna` |
| `Estilos_GrillasConPisoYTitulosAlineadosConSusDatos` | Piso de alto y títulos alineados (CSS) |
| `Tablas_SinFilasCentradasNiSangriaArmadaAMano` | Sin filas centradas ni sangría a mano |
| `Escala_TitulosDePaginaConCabeceraYH4SoloEnLaRaizDelMenu` | `CabeceraPagina` en toda página, H4 solo en el menú, H6 con `TagName.H2` |
| `Escala_UnaBarraUnTamano_BotonesSmallEnLasAcciones` | Botones Small en `<Acciones>` y en `FiltrosGrilla` |
| `Escala_SeparacionesEnMultiplosDe4px` | `Gap`, separaciones inline y del CSS en múltiplos de 4 px |
| `Escala_SinFuentesNiAnchosSueltos` | Sin `font-size`/`font-weight`/`max-width` inline; diálogos con `AnchoDialogo` |
| `Reconexion_AvisoPropioEnCastellanoParaCadaEstado` | Aviso de reconexión propio para cada estado |
| `Foco_AlNavegarEnElTituloSinRecuadro` | El foco al navegar va al H1, sin recuadro |
| `Enlaces_SinAnclaSuelta` | Sin `href="#…"`; el skip-link con la ruta actual y foco al contenido |
| `Estilos_SinColoresFijosPorNombre` | Sin `gray`/`black`/`white`… en `app-blazor.css` |
| `Celdas_EnTextoSinBadgesNiColorDeEstado` | Sin badges ni íconos en color en celdas; sin color de texto en los listados |

`GrillasTests` cubre el comportamiento de los listados (búsqueda, estado entre navegaciones,
página, filtros, opciones del filtro y pie).
