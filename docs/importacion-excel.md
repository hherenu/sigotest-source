# Importación desde Excel — estructuras de costos y certificados

Las planillas que llegan a CCyR son plantillas de SBASE con formato estable entre
contratistas: la **planilla de desglose** del Anexo VII (oferta), la **planilla de
cotización** que la resume, el **balance** de un BED y la hoja **CERTIFICADO** que
arma CCyR cada mes. Las reglas de abajo salen de planillas reales de tres obras
(Agüero, Lacroze/Virreyes, Premetro Savio) y están fijadas en los tests de
`ImportadorEstructuraTests`, `ImportadorCertificadoTests` y `LectorExcelTests`.

Piezas (`src/Sigotest.Web/Services/Importacion`):

- `LectorExcel`: lee .xls, .xlsx y .xlsm con ExcelDataReader a una grilla en memoria
  (valores cacheados de fórmulas, formato numérico por celda, errores de Excel, columnas
  acotadas a 120, hasta 50.000 filas con datos por hoja, hojas ocultas marcadas).
- `TextoImport`: normalización de textos y helpers de encabezado y de fila de total
  compartidos por los dos importadores.
- `ImportadorEstructura`: parser puro de desglose / cotización / balance a filas
  clasificadas; `EstructuraService.ImportarItemsAsync` las persiste.
- `ImportadorCertificado`: parser puro de la hoja CERTIFICADO a secciones y matching
  contra los bloques; el resultado se vuelca en la grilla de carga, no se guarda.

La subida es el componente compartido `SelectorPlanilla` (`Components/Shared`), con
`InputFile` de Blazor y no `RadzenFileInput`, que manda el archivo entero como data URL en
un solo mensaje y obligaría a subir el límite de mensajes del hub: con `InputFile` viaja
por el circuito en partes, sin endpoint HTTP. Primero valida extensión y tamaño
(`LectorExcel.ValidarArchivo`, hasta `LectorExcel.TamanoMaximoBytes`, 12 MB, que
`OpenReadStream` vuelve a exigir en el servidor): un archivo rechazado no toca la planilla
vigente. Si lo admite, avisa a la página antes de leer, para que nunca quede a la vista el
libro anterior con el nombre del nuevo. Descarta una lectura vieja que termina tarde y
cancela la lectura si se cierra el diálogo o se sale de la página. Se puede volver a elegir
el mismo archivo corregido porque el script de `InputFile` limpia el input en cada clic;
después de leer o de «Quitar», el foco vuelve al selector.

Cada importador evalúa todas las hojas del libro (`EvaluarHojas`) para el desplegable: lo
que reconoció en cada una, las marcas «oculta» y «recortada a 50.000 filas», y la hoja a
preseleccionar. Una hoja que no se puede analizar queda como «no se pudo analizar» (el
error se registra) y no impide usar las demás.

## Estructura de costos (`/estructuras/{id}/importar`)

**Encabezado por contenido.** Fila con `Item` o `Rubro` y `Descripción` (sin acentos ni
mayúsculas). Las columnas se toman por nombre: `U`, `Cantidad`, `Precio/Valor Unitario`,
`Subtotal`, `TOTAL`; las de USD se ignoran. Puede haber columnas antes del código.

**Hoja.** Se preseleccionan antes las hojas con ítems: una con encabezado pero nada
debajo (el «RESUMEN $» de Premetro) se marca «sin ítems» y solo se elige si no hay otra
con ítems (un balance al que le falta el grupo DEMASIA cuenta como con ítems: se usa
eligiendo otro grupo). Entre ellas, la primera visible con desglose o balance; si no hay, la de
cotización. Las ocultas se marcan en el desplegable y van después de las visibles (el
balance del BED de Agüero esconde « Balance EDyA» con encabezado de desglose). En el
balance, el grupo Contrato también exige su columna Cantidad; si falta, se avisa igual que
cuando falta la del grupo elegido.

| Layout | Cómo se reconoce | Particularidad |
| --- | --- | --- |
| Desglose | Hay columna de precio unitario | Rubros con valor en TOTAL |
| Cotización | No hay precio unitario | PU = Subtotal ÷ Cantidad |
| Balance | Encabezado de dos filas con grupos ECONOMIA / DEMASIA / BED | Se elige el grupo que aporta cantidad y PU (Demasía por defecto) |

**Clasificación de filas** (cada una se muestra en la vista previa antes de importar):

- Se ignoran las filas sin descripción, las de solo un guion, los códigos sin
  descripción y los separadores (`ÍTEMS NUEVOS` o `ITEM NUEVOS`, `DEMASÍAS`, `ECONOMÍAS`,
  `ADICIONALES`, `BALANCE`).
- **Rubro**: sin cantidad ni PU y (a) sin unidad y con un número en la celda TOTAL, aunque
  sea 0 (un guion o un texto no cuentan), o (b) su código es prefijo (más un punto) del
  siguiente código. Un rubro codificado cuelga del rubro cuyo código es prefijo del suyo:
  el abierto más cercano o, si aparece fuera de orden (A.1.5 después de A.2), el ya visto
  con el prefijo más largo. Sin código, nace en la raíz.
- **Sub-rubro**: sin código, sin unidad, sin cantidad ni PU. Cuelga del rubro vigente;
  dos consecutivos quedan como hermanos.
- **Ítem**: todo lo demás. Cuelga del agrupador vigente, salvo que su código diga otra
  cosa: si el agrupador con código más cercano no es prefijo del suyo y un rubro ya visto
  sí lo es, cuelga de ese (ítems agregados fuera de orden). «Ya visto» es en la misma
  sección y después del último separador. Sin cantidad o sin precio (los «No aplica») se
  importa con monto vacío, y con alguno en cero, con monto 0; sin código se importa y se
  marca. Cantidad y PU se redondean a 4 decimales y el monto se recalcula con la regla de
  la app (no se toma el subtotal de la planilla).
- Un error de Excel (`#REF!`, `#DIV/0!`…) en Cantidad o PU (o en Subtotal, en la
  cotización) se toma como celda vacía y se avisa, pero la fila sigue siendo un ítem: los
  rubros no tienen números. En la columna del código, un error no es un código.
- Los códigos repetidos se avisan. Las descripciones de más de 500 caracteres y los códigos
  o unidades de más de 250 (el largo de sus columnas) se recortan con aviso.
- Balance con cualquier grupo que no sea Contrato: los ítems sin cantidad en el grupo no se
  importan (los que solo tienen cantidad en Economía se marcan como economía: se certifican
  como % negativo sobre el bloque básico) y los rubros que quedan sin ítems tampoco. Con el
  grupo Demasía, los existentes se marcan `Demasia` y los `N-…` o sin cantidad de contrato,
  `Adicional`.

**Secciones.** Cada fila `TOTAL …` cierra una sección (la cotización trae una por
bloque). También cierra un subtotal sin la palabra: código y descripción vacíos, el
nombre del bloque en una columna numérica y el monto en Subtotal. Se eligen cuáles
importar (la lista muestra el título y las filas de cada una). El total de la planilla se
contrasta con el calculado solo si todas las secciones elegidas cierran con un total: una
suma parcial mostraría una diferencia que no existe.

**Persistencia.** Agregar al final (siempre) o Reemplazar todo (solo si ningún
certificado tiene un bloque sobre la estructura y ningún ítem de otra estructura, un BED,
toma uno de estos como origen). Los agrupadores se guardan sin código, como el resto de la
app. El RowVersion de la página viaja como token de concurrencia de la estructura; como no
cambia al agregar o quitar ítems, Reemplazar exige además que el conjunto de ítems sea el
que la página vio. La transacción es serializable porque esos chequeos miran filas que el
RowVersion no protege. Con Agregar, la página avisa los códigos de la planilla que ya están
en la estructura: quedarían repetidos y la importación de certificados no aplicaría esos
ítems (un código repetido en el bloque es ambiguo).

## Certificado (botón «Importar % desde Excel» en la carga)

**Hoja.** Candidatas: las que tienen encabezado `Item` + `Descripción` + columna `Actual`.
Los libros reales traen varias (Premetro: un «RESUMEN $» sin renglones y la hoja en
dólares; Agüero desde el BED: copias ocultas de meses anteriores y hojas paralelas en
cero con el mismo encabezado), así que se preselecciona la que se analiza sin error,
coincide en N° y período con el certificado, está visible y tiene más renglones y más %
del mes cargados. El desplegable muestra el N° y el período leídos y marca las ocultas;
si otra hoja tiene el mismo N° y período y una cantidad parecida de renglones, se avisa.
El período de la cabecera se lee como «MAY/25», «AGOSTO 2026» o «JUNIO DE 2026».
`Anterior / Actual / Acumulado` pueden estar en la misma fila del encabezado o en la
siguiente, bajo `ACTA DE MEDICIÓN`. El primer `Actual` es el % del acta; el segundo, el
monto del certificado.

**Porcentajes.** Si la celda es un número con formato `%`, el valor crudo es una fracción
y se multiplica por 100; si no, se toma como puntos. Un `%` literal del formato
(`0.00"%"` o `0.00\%`) no cuenta: Excel lo muestra sin multiplicar. Un texto «12,5%» o
«0.5%» ya está en puntos aunque la celda tenga formato `%` (Excel es-AR deja como texto lo
tipeado con punto). Vacío = 0 %; un texto no numérico («s/d»), un error de Excel o un % de
más de ±1000 dejan el renglón sin aplicar. Solo se importa `Actual`.

**Secciones.** Título (fila anterior al encabezado o texto suelto como `DEMASIAS`),
encabezado propio o heredado, renglones con código y datos de contrato (unidad, cantidad
o subtotal; un rubro con ceros residuales en los % no es renglón), y cierre en la fila
que contiene la palabra TOTAL fuera de la descripción (puede ir al final del texto:
`… - TOTAL C/ IVA`). Los totales generales y el resumen de anticipo que siguen se ignoran.
Una sección sin encabezado propio que sigue a un total hereda el título del tramo
(«DEMASIAS (cont.)»); dentro de un tramo de demasías, un título neutro queda como
subtítulo («DEMASIAS › GENERALES DE PROYECTO»). Un título de economías corta el tramo, y
también el total que lo nombra («TOTAL DEMASIAS»; un subtotal no): lo que siga sin título
propio queda «Sección N». Entre dos secciones, de varios textos candidatos a título gana
el primero, salvo que uno posterior hable de demasías o de economías y el primero no.

**Aplicación.** Cada sección se mapea a un bloque del certificado. Por defecto se sugiere
el bloque que contiene al menos dos tercios de sus códigos; con empate o sin bloque que
llegue, queda en «No importar». Una sección que dice demasías/BED/adicional solo se
sugiere en un bloque que también lo dice, porque las demasías repiten los códigos del
básico; si el certificado no tiene ninguno, el diálogo avisa que hay que agregarlo con
«Agregar bloque». Varias secciones pueden ir al mismo bloque y se unen antes de aplicar (una
estación por sección, subtotales por rubro). El matching es por código
normalizado **dentro del bloque**: los códigos se repiten entre secciones (generales
prorrateados por estación, demasías del BED). Un código repetido dentro del bloque no se
aplica; un código sin ítem se lista; los ítems sin renglón no se tocan. El `Anterior`
de la planilla se contrasta con el del sistema y las diferencias se avisan sin bloquear.
También se contrastan número y período de la cabecera `CERTIFICADO N°… - período` y el
monto del mes de la fila TOTAL contra el calculado (si se unen secciones y alguna no
cierra con un total, no hay monto de planilla para contrastar).

## Fuera de alcance

- Componente en dólares (hoja «CERTIFICADO EN DOLARES»): los ítems de estructura no
  tienen moneda.
- `ItemOrigenId` de los ítems de un BED hacia el básico: el modelo lo admite, la
  importación no lo completa.
