# Importación de publicaciones INDEC — pegado desde Excel

Los valores mensuales de los índices entran por `/indices/importar` (rol Certificaciones)
pegando filas copiadas de un Excel que mantiene el equipo. La app no interpreta los
cuadros de INDEC: el formato lo define la planilla, y la app valida contra el catálogo y la
base antes de insertar. Las reglas de parseo están en `Services/ImportadorIndec.cs` y sus
tests en `ImportadorIndecTests`; el análisis y la persistencia en `IndiceService`.

Decisión 2026-10-07: se descartó un parser de los cuadros de la revista copiados del PDF
(rama `feature/import-indices`, no mergeada): para 40 índices y un valor por mes, una
planilla curada a mano es más confiable que heurísticas sobre texto, y una carga externa
directa a la base (Apache Hop) saltearía rol, auditoría y validaciones de la app.

## Flujo

```
 Excel maestro (una pestaña por publicación, curada a mano)
 ┌──────────────┬──────┬─────┬──────────┐
 │ CÓDIGO       │ AÑO  │ MES │ VALOR    │   4 columnas: cada fila trae su período
 │ MANO_OBRA    │ 2026 │  8  │ 11.129,8 │
 │ CEMENTO_CAL  │ 2026 │  8  │ 10.819,3 │   (o 2 columnas CÓDIGO · VALOR y el período
 └──────────────┴──────┴─────┴──────────┘    lo da el año/mes de la pantalla)
        │  copiar y pegar en el textarea
        ▼
 /indices/importar: ID publicación (obligatorio) · año/mes por defecto · [Analizar]
        │  1. Analizar (no escribe en la base)
        ▼
 ImportadorIndec.ParsearLineas (puro)
   · separa por TAB o «;»; 2 o 4 columnas, si no → «Fila inválida»
   · decimal: con coma = es-AR («11.129,8»); sin coma = punto («11129.8»)
   · valor cero o negativo → «Fila inválida» (IndiceValidator.ValorMensual)
        ▼
 IndiceService.AnalizarImportacionAsync (solo lectura)
   · código no está en el catálogo        → «Código desconocido» (botón Agregar al catálogo)
   · mismo código y período dos veces     → «Repetida en el pegado»
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

1. Nueva pestaña en el Excel maestro con las cuatro columnas (40 filas por publicación).
2. Copiar el rango, pegar, Analizar, revisar la vista previa, Importar.
3. Históricos: mismo formato con más filas; el índice único impide duplicar.
