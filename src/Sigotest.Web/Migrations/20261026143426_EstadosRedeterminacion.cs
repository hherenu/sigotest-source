using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIGO.Migrations
{
    /// <summary>
    /// Estado del trámite de los disparos. (1) El número de disparo es único solo entre los
    /// vigentes: uno rechazado o anulado no consume número y el que lo reemplaza usa el
    /// mismo. (2) Datos: hasta ahora el estado quedaba siempre en Calculada aunque el
    /// disparo tuviera expediente VR o fechas de aprobación, y se podía recalcular o
    /// eliminar. Se le asigna el estado que corresponde a esos datos (misma regla que
    /// RedeterminacionValidator.EstadoAdministrativo). Idempotente: solo toca Borrador o
    /// Calculada. Down vuelve al índice sin filtro (falla si hay un dado de baja con el
    /// mismo número que uno vigente) y no revierte los estados (son datos válidos).
    /// </summary>
    public partial class EstadosRedeterminacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RedeterminacionesGuardadas_ObraId_NroDisparo",
                table: "RedeterminacionesGuardadas");

            migrationBuilder.CreateIndex(
                name: "IX_RedeterminacionesGuardadas_ObraId_NroDisparo",
                table: "RedeterminacionesGuardadas",
                columns: new[] { "ObraId", "NroDisparo" },
                unique: true,
                filter: "[Estado] <> 'Rechazada' AND [Estado] <> 'Anulada'");

            migrationBuilder.Sql(@"
UPDATE RedeterminacionesGuardadas
SET Estado = CASE
    WHEN FechaAprobacionOS IS NOT NULL THEN 'AprobadaOS'
    WHEN FechaAprobacionCCyR IS NOT NULL THEN 'AprobadaCCyR'
    ELSE 'Presentada' END
WHERE Estado IN ('Borrador', 'Calculada')
  AND (FechaAprobacionOS IS NOT NULL OR FechaAprobacionCCyR IS NOT NULL
       OR LTRIM(RTRIM(ISNULL(NroExpedienteVR, ''))) <> '');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RedeterminacionesGuardadas_ObraId_NroDisparo",
                table: "RedeterminacionesGuardadas");

            migrationBuilder.CreateIndex(
                name: "IX_RedeterminacionesGuardadas_ObraId_NroDisparo",
                table: "RedeterminacionesGuardadas",
                columns: new[] { "ObraId", "NroDisparo" },
                unique: true);
        }
    }
}
