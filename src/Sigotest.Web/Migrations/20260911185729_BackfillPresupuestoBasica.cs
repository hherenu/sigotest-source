using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIGO.Migrations
{
    /// <summary>
    /// Migración de datos, sin cambio de esquema. Antes de los presupuestos por obra, el
    /// autorizado de la Obra Básica era una fila MontoAutorizado del plan; ahora es el
    /// presupuesto de la obra y esa fila se ignora y se borra en el próximo guardado de
    /// la grilla. Para no perder el importe ni dejar los planes existentes sin contra
    /// qué balancear, se copia al presupuesto oficial (por moneda) de las obras que
    /// todavía no lo tienen cargado. Idempotente: solo toca obras con oficial en 0/NULL.
    /// Down no revierte (los importes copiados son datos válidos de la obra).
    /// </summary>
    public partial class BackfillPresupuestoBasica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE o SET PresupuestoOficial = x.Monto
FROM Obras o
CROSS APPLY (
    SELECT TOP 1 m.Monto
    FROM Planificaciones p
    JOIN Autorizantes a ON a.PlanificacionId = p.Id
    JOIN PlanMontos m ON m.AutorizanteId = a.Id
    WHERE p.ObraId = o.Id AND a.Tipo = 'Basica'
      AND m.Concepto = 'MontoAutorizado' AND m.Moneda = 'Pesos' AND m.Monto > 0
) x
WHERE o.PresupuestoOficial = 0;

UPDATE o SET PresupuestoOficialUSD = x.Monto
FROM Obras o
CROSS APPLY (
    SELECT TOP 1 m.Monto
    FROM Planificaciones p
    JOIN Autorizantes a ON a.PlanificacionId = p.Id
    JOIN PlanMontos m ON m.AutorizanteId = a.Id
    WHERE p.ObraId = o.Id AND a.Tipo = 'Basica'
      AND m.Concepto = 'MontoAutorizado' AND m.Moneda = 'USD' AND m.Monto > 0
) x
WHERE o.PresupuestoOficialUSD IS NULL;

UPDATE o SET PresupuestoOficialEUR = x.Monto
FROM Obras o
CROSS APPLY (
    SELECT TOP 1 m.Monto
    FROM Planificaciones p
    JOIN Autorizantes a ON a.PlanificacionId = p.Id
    JOIN PlanMontos m ON m.AutorizanteId = a.Id
    WHERE p.ObraId = o.Id AND a.Tipo = 'Basica'
      AND m.Concepto = 'MontoAutorizado' AND m.Moneda = 'EUR' AND m.Monto > 0
) x
WHERE o.PresupuestoOficialEUR IS NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sin reversión: los importes copiados son datos válidos de la obra.
        }
    }
}
