using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIGO.Migrations
{
    /// <summary>
    /// Presupuestos oficial y adjudicado en dólares y euros. Los importes en pesos ya
    /// los agregó <see cref="PresupuestosObra"/>; esta migración solo suma las cuatro
    /// columnas de las otras monedas (por eso el Designer difiere del Up: el modelo
    /// destino es el completo).
    /// </summary>
    public partial class PresupuestosObraMonedas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PresupuestoAdjudicadoEUR",
                table: "Obras",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PresupuestoAdjudicadoUSD",
                table: "Obras",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PresupuestoOficialEUR",
                table: "Obras",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PresupuestoOficialUSD",
                table: "Obras",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PresupuestoAdjudicadoEUR",
                table: "Obras");

            migrationBuilder.DropColumn(
                name: "PresupuestoAdjudicadoUSD",
                table: "Obras");

            migrationBuilder.DropColumn(
                name: "PresupuestoOficialEUR",
                table: "Obras");

            migrationBuilder.DropColumn(
                name: "PresupuestoOficialUSD",
                table: "Obras");
        }
    }
}
