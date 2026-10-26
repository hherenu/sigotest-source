using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SIGO.Data;
using SIGO.Models;
using SIGO.Models.Enums;

namespace SIGO.Tests;

/// <summary>
/// Las migraciones contra una LocalDB vacía. Producción las aplica al arrancar
/// (Program.cs) y el resto de los tests crea la base con EnsureCreated, que no las
/// ejecuta: un cambio de modelo sin su migración pasaba el CI y la app no levantaba.
/// </summary>
public class MigracionesTests
{
    private const string MigracionAnteriorAEstados = "20260911185729_BackfillPresupuestoBasica";

    [Fact]
    public void ElModeloNoTieneCambiosSinMigracion()
    {
        // No abre conexión: compara el modelo con el snapshot de las migraciones.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(@"Server=(localdb)\mssqllocaldb;Database=SinUso;Trusted_Connection=True").Options);

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task EstadosRedeterminacion_AsignaElEstadoQueCorrespondeALosDatosAdministrativos()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($@"Server=(localdb)\mssqllocaldb;Database=MigracionesTests_{Guid.NewGuid():N};Trusted_Connection=True;MultipleActiveResultSets=true")
            .Options;
        await using var db = new AppDbContext(options);
        try
        {
            // Base en la migración anterior, con disparos como los dejaba la app: todos
            // Calculada, tuvieran o no expediente y fechas de aprobación.
            var migrador = db.GetService<IMigrator>();
            await migrador.MigrateAsync(MigracionAnteriorAEstados);

            var obra = new Obra { Nombre = "Obra migración", NumeroLicitacion = "LP MIG" };
            db.Obras.Add(obra);
            await db.SaveChangesAsync();
            var tabla = new TablaPonderacion { ObraId = obra.Id, Nombre = "Tabla" };
            db.TablasPonderacion.Add(tabla);
            await db.SaveChangesAsync();
            RedeterminacionGuardada Disparo(int nro, string? expediente = null, DateTime? ccyr = null, DateTime? os = null) => new()
            {
                ObraId = obra.Id, TablaPonderacionId = tabla.Id, NroDisparo = nro, Estado = EstadoRedeterminacion.Calculada,
                NroExpedienteVR = expediente, FechaAprobacionCCyR = ccyr, FechaAprobacionOS = os
            };
            db.RedeterminacionesGuardadas.AddRange(
                Disparo(1, "EX-2025-1-GCABA-SBASE", new DateTime(2025, 3, 1), new DateTime(2025, 4, 1)),
                Disparo(2, ccyr: new DateTime(2025, 8, 1)),
                Disparo(3, "EX-2026-2-GCABA-SBASE"),
                Disparo(4, "  "));
            await db.SaveChangesAsync();

            await migrador.MigrateAsync();

            await using var verificacion = new AppDbContext(options);
            var estados = await verificacion.RedeterminacionesGuardadas.AsNoTracking()
                .Where(r => r.ObraId == obra.Id).OrderBy(r => r.NroDisparo).Select(r => r.Estado).ToListAsync();
            Assert.Equal(
                new[] { EstadoRedeterminacion.AprobadaOS, EstadoRedeterminacion.AprobadaCCyR,
                        EstadoRedeterminacion.Presentada, EstadoRedeterminacion.Calculada },
                estados);
            Assert.Empty(await verificacion.Database.GetPendingMigrationsAsync());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
