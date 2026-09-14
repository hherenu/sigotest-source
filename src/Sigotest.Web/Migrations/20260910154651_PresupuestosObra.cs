using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SIGO.Data;

#nullable disable

namespace SIGO.Migrations
{
    /// <summary>
    /// Presupuestos oficial y adjudicado de la obra en pesos. Sin Designer a propósito:
    /// esta migración ya se aplicó en bases de desarrollo con este mismo id antes de
    /// que se sumaran las otras monedas (<see cref="PresupuestosObraMonedas"/>), así
    /// que se conserva tal cual para que el historial coincida; el modelo destino
    /// completo lo lleva la migración siguiente y el snapshot.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260910154651_PresupuestosObra")]
    public partial class PresupuestosObra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PresupuestoAdjudicado",
                table: "Obras",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PresupuestoOficial",
                table: "Obras",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PresupuestoAdjudicado",
                table: "Obras");

            migrationBuilder.DropColumn(
                name: "PresupuestoOficial",
                table: "Obras");
        }
    }
}
