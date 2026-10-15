# Importación de publicaciones INDEC — plantilla Excel o pegado

Los valores mensuales de los índices entran por `/indices/importar` (rol Certificaciones) de
dos maneras: subiendo la **plantilla Excel** que genera la app, con la fila de valores de
cada índice pegada del PDF de la revista, o pegando filas copiadas de un Excel propio. La
app no interpreta los cuadros de INDEC: el usuario elige qué fila del PDF corresponde a cada
índice, y la app valida contra el catálogo y la base antes de insertar. Las reglas de
lectura están en `Services/Importacion/PlantillaIndices.cs` (plantilla) y
`Services/ImportadorIndec.cs` (pegado), con sus tests en `PlantillaIndicesTests` e
`ImportadorIndecTests`; el análisis y la persistencia en `IndiceService`.

## Plantilla Excel

Mismo formato que el Excel maestro provisorio (`artifacts/plantillas`), generado por la app
con el catálogo al día.

1. En `/indices/importar`, completar el ID de publicación y «Descargar plantilla»
   (`GET /Indices/DescargarPlantilla?idPublicacion=…`, rol Certificaciones). La hoja «Carga»
   trae el ID en la celda «ID publicación», una fila por índice del catálogo vigente
   (código, familia, índice normalizado, cuadro e inciso como referencia) salvo
   `COSTO_FINANCIERO`, que va sin publicación (ver más abajo), la columna
   «Pegado del PDF (fila de valores)» y una columna por cada uno de los 13 meses que muestra
   la revista: del mismo mes del año anterior al mes previo a la publicación
   (`INDEC_INFORMA_07_26` → jun-25 … jun-26).
2. Para cada índice, copiar del PDF su fila de valores (desde el primer valor después del
   «100,0» hasta el último mes) y pegarla en «Pegado del PDF»: queda en una sola celda y las
   fórmulas de las columnas de mes la reparten, un valor por mes. Si se copia la fila entera
   con la descripción y la base, se descarta todo lo anterior al «100,0»; una «s» (secreto
   estadístico) deja ese mes vacío. Para cargar un valor suelto o corregir uno, se tipea en
   la celda del mes: reemplaza la fórmula y tiene prioridad. Excel no deja tipear un valor
   que no sea un número mayor que cero.
3. Subirla con «Elegir planilla». El ID de publicación es el del archivo (no se edita
   mientras la planilla esté cargada) y los valores pasan por el mismo análisis y la misma
   vista previa que el pegado. Mientras haya un archivo elegido no se ofrece el pegado; al
   quitarlo vuelve la sección plegable «¿Preferís pegar filas?».

La página sigue el esquema de Importar ítems: arriba una tarjeta con los tres pasos
(publicación, plantilla, revisar e importar) y debajo la vista previa a todo el ancho.

La lectura no depende de las fórmulas: reparte el pegado en C# con las mismas reglas
(`PlantillaIndices.RepartirPegado`), así que da lo mismo un archivo guardado sin recalcular
o abierto en otro programa. El valor de cada mes es el número de su celda si tiene uno
(tipeado, o el que calculó Excel) y, si está vacía, el del pegado. Las fórmulas usan
`NUMBERVALUE` con la coma decimal, que no depende del idioma de Excel.

Reglas de la lectura:

- Ubica la publicación, la columna «CÓDIGO», la del pegado y los meses (celdas con fecha en
  el encabezado) por su rótulo: insertar filas o columnas no la rompe. Una hoja muy oculta
  con una marca distingue la plantilla de cualquier otro Excel.
- Rechaza el archivo entero, con el motivo, si falta el ID, la columna del pegado o los
  meses; si un mes está en dos columnas; si hay un segundo encabezado; o si una columna
  con números no tiene fecha en el encabezado (un mes cuyo rótulo se borró perdería sus
  valores sin aviso).
- «Fila inválida», con el motivo: un pegado con más o menos valores que meses (cada valor
  caería en el mes equivocado) o con algo que no es un valor; un texto o un error de Excel
  en un mes; un valor que no es mayor que cero; una fila con valores y sin código; una fila
  de `COSTO_FINANCIERO` agregada a mano (con la publicación de la plantilla le ganaría a la
  tasa sin publicación en el cálculo de esa publicación).
- Un código agregado a mano que no está en el catálogo sale «Código desconocido», como en
  el pegado.

Decisión 2026-10-07: se descartó un parser de los cuadros de la revista copiados del PDF
(rama `feature/import-indices`, no mergeada): para 40 índices, que el usuario elija la fila
de cada uno es más confiable que heurísticas sobre el texto de los cuadros, y una carga
externa directa a la base (Apache Hop) saltearía rol, auditoría y validaciones de la app.
La plantilla solo reparte una fila de valores ya elegida.

## Flujo

```
 A) Plantilla Excel                          B) Pegado
 [Descargar plantilla] → pegar la fila del   filas de un Excel propio: 4 columnas
 PDF en cada índice → [Elegir planilla]      CÓDIGO · AÑO · MES · VALOR, o 2 columnas
 (la publicación es la del archivo)          CÓDIGO · VALOR con el año/mes de la pantalla
        │                                           │
        ▼                                           ▼
 PlantillaIndices.Leer (puro)                ImportadorIndec.ParsearLineas (puro)
   · reparte el pegado en los meses            · separa por TAB o «;»; 2 o 4 columnas
   · celda de mes con número: manda            · decimal: con coma = es-AR («11.129,8»);
   · período: fecha del encabezado               sin coma = punto («11129.8»)
   · pegado mal, texto o valor ≤ 0             · valor ≤ 0 → «Fila inválida»
     → «Fila inválida»
        └─────────────────────┬─────────────────────┘
                              │  1. Analizar (no escribe en la base; ID de publicación obligatorio)
                              ▼
 IndiceService.AnalizarImportacionAsync (solo lectura)
   · código no está en el catálogo        → «Código desconocido» (botón Agregar al catálogo)
   · mismo código y período dos veces     → «Repetida»
   · ya cargado en ESA misma publicación  → «Ya existe» (se omite)
   · el resto                             → «A importar»
        ▼
 Vista previa: badges + grilla por fila
        │  2. [Importar N valores]  (solo las filas «A importar»)
        ▼
 IndiceService.ImportarAsync: rol, ID de publicación obligatorio, un solo SaveChanges
 (entra todo o nada); red final: índice único (índice, año, mes, publicación) en SQL.
```

## Publicación

Cada valor se guarda con el ID de la publicación de la que salió (`INDEC_INFORMA_MM_AA`).
INDEC revisa meses anteriores en publicaciones posteriores, así que el mismo índice y mes
puede tener un valor por publicación. El cálculo de redeterminaciones
(`RedeterminacionService`) elige así: si se pide una publicación, solo mira los valores de
esa publicación y los que no tienen publicación, prefiriendo los de la publicación; si no
se pide ninguna, toma el valor sin publicación y, si no hay, el de la publicación más
reciente por período (`PublicacionIndec.Orden`).

El ID es obligatorio en la importación y debe tener el formato `INDEC_INFORMA_MM_AA`
(`IndiceValidator.PublicacionImportacion`, exigido en la página antes de analizar y en el
servicio): sin ID los valores quedaban invisibles como publicación en Calcular y mezclados
con los valores base; con un ID no fechable la publicación nunca ganaba como más reciente.

Los valores sin publicación (hoy, las tasas activas del Banco Nación, índice
`COSTO_FINANCIERO`) valen para cualquier publicación y se cargan de a uno desde «Valores
mensuales» del índice, que sí admite publicación vacía.

## Catálogo

- `Código ID` es la clave del pegado (match exacto, sin distinguir mayúsculas).
- `Familia de Recurso` es el nombre con el que las tablas de ponderación eligen el índice.
- `Índice Normalizado`, `Cuadro de Referencia` e `Inciso` son informativos: ubican el
  índice en la revista INDEC para armar la planilla.
- Una fila «Código desconocido» se resuelve con «Agregar al catálogo» en la vista previa
  (`IndiceCrearDialog`): crea el índice con el código precargado y re-analiza el pegado.

## Lo que la importación no hace

Solo inserta. No actualiza un valor existente ni borra: una corrección de un mes ya
cargado en la misma publicación sale «Ya existe» y se omite; se corrige desde la página de
valores del índice. Recargar una publicación entera exige borrar antes sus valores, de a uno,
desde «Valores mensuales» de cada índice: no hay borrado por publicación.

## Trabajo mensual

1. Descargar la plantilla con el ID de la publicación nueva.
2. Pegar en cada índice su fila de valores del PDF; controlar «Valores en los meses» arriba
   de la hoja (con la revista completa, 13 por índice).
3. Subirla, revisar la vista previa e Importar. Guardar el archivo completado sirve como
   registro de lo que se importó (la app no lo guarda).
4. Históricos o cargas sueltas: el pegado de 4 columnas sigue disponible; el índice único
   impide duplicar.
