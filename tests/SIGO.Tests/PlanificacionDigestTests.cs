using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SIGO.Data;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Services;

namespace SIGO.Tests;

/// <summary>
/// Digest mensual a los directores: sale después del rollover del mes (informa el ciclo
/// nuevo), una sola vez aunque corran dos procesos, y si fallan todos los envíos la
/// pasada siguiente reintenta. Cada test usa su propio mes ficticio para no chocar con
/// los demás.
/// </summary>
public class PlanificacionDigestTests(LocalDbFixture fx) : IClassFixture<LocalDbFixture>
{
    /// <summary>Correo que registra lo enviado; <paramref name="falla"/> decide qué direcciones no salen.</summary>
    private sealed class CorreoFalso(Func<string, bool>? falla = null) : IEmailSender
    {
        public List<(string Para, string Cuerpo)> Enviados { get; } = [];

        public Task<int> EnviarAsync(IReadOnlyCollection<string> destinatarios, string asunto, string cuerpoHtml)
        {
            var salen = destinatarios.Where(d => falla?.Invoke(d) != true).ToList();
            Enviados.AddRange(salen.Select(d => (d, cuerpoHtml)));
            return Task.FromResult(salen.Count);
        }
    }

    /// <summary>Justo antes de la reserva de este proceso, otro reserva el mismo mes.</summary>
    private sealed class OtroProcesoReservaPrimero(DbContextOptions<AppDbContext> opciones, DateTime mes) : DbCommandInterceptor
    {
        private bool reservado;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!reservado && command.CommandText.Contains("INSERT INTO [DigestsPlanificacionEnviados]"))
            {
                reservado = true;
                await using var otro = new AppDbContext(opciones);
                otro.DigestsPlanificacionEnviados.Add(new DigestPlanificacionEnviado
                {
                    Anio = mes.Year, Mes = mes.Month, FechaEnvio = DateTime.UtcNow, CantidadMails = DigestPlanificacionEnviado.EnCurso
                });
                await otro.SaveChangesAsync(cancellationToken);
            }
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>La base se corta: falla la consulta de obras y también un intento de liberar la reserva.</summary>
    private sealed class FallaLaBase : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [Obras]")
                || (command.CommandText.Contains("DELETE") && command.CommandText.Contains("[DigestsPlanificacionEnviados]")))
                throw new InvalidOperationException("Corte simulado de la base.");
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>Obra en planificación con un director propio y su plan en el estado pedido; devuelve el correo.</summary>
    private async Task<string> CrearObraConPlanAsync(EstadoPlanificacion estado)
    {
        var correo = $"director.{Guid.NewGuid():N}@test.local";
        await using var db = fx.CrearContexto();
        var obra = new Obra
        {
            Nombre = $"Obra digest {Guid.NewGuid():N}", NumeroLicitacion = "LP DIG",
            FechaFinalContrato = new DateTime(2099, 12, 1), CorreoDirectorObra = correo
        };
        db.Obras.Add(obra);
        await db.SaveChangesAsync();
        db.Planificaciones.Add(new Planificacion { ObraId = obra.Id, Estado = estado });
        await db.SaveChangesAsync();
        return correo;
    }

    private Task<int?> DigestAsync(CorreoFalso correo, DateTime hoy) => Ejecutar(async db =>
        await PlanificacionDigestService.EjecutarAsync(db, correo, null, hoy, NullLogger.Instance));

    private Task<int?> RolloverAsync(DateTime hoy) => Ejecutar(db => PlanificacionRolloverService.EjecutarAsync(db, hoy));

    private async Task<int?> Ejecutar(Func<SIGO.Data.AppDbContext, Task<int?>> accion)
    {
        await using var db = fx.CrearContexto();
        return await accion(db);
    }

    private async Task<bool> RegistradoAsync(DateTime mes)
    {
        await using var db = fx.CrearContexto();
        return await db.DigestsPlanificacionEnviados.AnyAsync(d => d.Anio == mes.Year && d.Mes == mes.Month);
    }

    [Fact]
    public async Task SinElRolloverDelMes_NoSaleNiQuedaRegistrado()
    {
        await CrearObraConPlanAsync(EstadoPlanificacion.Aprobada);
        var correo = new CorreoFalso();
        var hoy = new DateTime(2094, 1, 10);

        Assert.Null(await DigestAsync(correo, hoy));

        Assert.Empty(correo.Enviados);
        Assert.False(await RegistradoAsync(hoy));
    }

    [Fact]
    public async Task DespuesDelRollover_InformaLosEstadosDelCicloNuevo_YNoSeRepite()
    {
        var director = await CrearObraConPlanAsync(EstadoPlanificacion.Aprobada);
        var correo = new CorreoFalso();
        var hoy = new DateTime(2094, 2, 10);

        // Con la app apagada el 9 y el 10, al arrancar corren los dos: el digest espera al
        // rollover y muestra el plan ya reiniciado, no el «Aprobada» del ciclo anterior.
        Assert.Null(await DigestAsync(correo, hoy));
        Assert.NotNull(await RolloverAsync(hoy.AddDays(-1)));
        var enviados = await DigestAsync(correo, hoy);

        Assert.True(enviados >= 1);
        var cuerpo = Assert.Single(correo.Enviados, m => m.Para == director).Cuerpo;
        Assert.Contains("Pendiente de carga", cuerpo);
        Assert.DoesNotContain("Aprobada", cuerpo);
        Assert.True(await RegistradoAsync(hoy));

        Assert.Null(await DigestAsync(correo, hoy));
        Assert.Single(correo.Enviados, m => m.Para == director);
    }

    [Fact]
    public async Task SiFallanTodosLosEnvios_NoQuedaRegistrado_YLaPasadaSiguienteReintenta()
    {
        var director = await CrearObraConPlanAsync(EstadoPlanificacion.Pendiente);
        var hoy = new DateTime(2094, 3, 10);
        Assert.NotNull(await RolloverAsync(hoy.AddDays(-1)));

        Assert.Null(await DigestAsync(new CorreoFalso(_ => true), hoy));
        Assert.False(await RegistradoAsync(hoy)); // la reserva se liberó

        var correo = new CorreoFalso();
        Assert.NotNull(await DigestAsync(correo, hoy));
        Assert.Contains(correo.Enviados, m => m.Para == director);
        Assert.True(await RegistradoAsync(hoy));
    }

    [Fact]
    public async Task ConExitoParcial_QuedaRegistradoConLosQueSalieron()
    {
        var falla = await CrearObraConPlanAsync(EstadoPlanificacion.Pendiente);
        var sale = await CrearObraConPlanAsync(EstadoPlanificacion.Pendiente);
        var hoy = new DateTime(2094, 4, 10);
        Assert.NotNull(await RolloverAsync(hoy.AddDays(-1)));

        var correo = new CorreoFalso(d => d == falla);
        var enviados = await DigestAsync(correo, hoy);

        Assert.Contains(correo.Enviados, m => m.Para == sale);
        await using var db = fx.CrearContexto();
        var registro = await db.DigestsPlanificacionEnviados.SingleAsync(d => d.Anio == hoy.Year && d.Mes == hoy.Month);
        Assert.Equal(enviados, registro.CantidadMails); // reintentar duplicaría los que ya salieron
    }

    [Fact]
    public async Task SiOtroProcesoReservaElMes_EsteNoEnvia()
    {
        var director = await CrearObraConPlanAsync(EstadoPlanificacion.Pendiente);
        var hoy = new DateTime(2094, 5, 10);
        Assert.NotNull(await RolloverAsync(hoy.AddDays(-1)));
        var conCarrera = new DbContextOptionsBuilder<AppDbContext>(fx.Options)
            .AddInterceptors(new OtroProcesoReservaPrimero(fx.Options, hoy)).Options;
        var correo = new CorreoFalso();

        await using (var db = new AppDbContext(conCarrera))
            Assert.Null(await PlanificacionDigestService.EjecutarAsync(db, correo, null, hoy, NullLogger.Instance));

        Assert.DoesNotContain(correo.Enviados, m => m.Para == director);
        Assert.True(await RegistradoAsync(hoy)); // la reserva del otro proceso
    }

    [Fact]
    public async Task SiLaBaseSeCortaAlArmarElDigest_ElMesNoQuedaReservado()
    {
        await CrearObraConPlanAsync(EstadoPlanificacion.Pendiente);
        var hoy = new DateTime(2094, 6, 10);
        Assert.NotNull(await RolloverAsync(hoy.AddDays(-1)));
        var conCorte = new DbContextOptionsBuilder<AppDbContext>(fx.Options).AddInterceptors(new FallaLaBase()).Options;

        await using (var db = new AppDbContext(conCorte))
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                PlanificacionDigestService.EjecutarAsync(db, new CorreoFalso(), null, hoy, NullLogger.Instance));
            Assert.Contains("Corte simulado", ex.ToString());
        }

        // Antes se reservaba primero y, si tampoco se podía liberar, el mes se perdía.
        Assert.False(await RegistradoAsync(hoy));
    }
}
