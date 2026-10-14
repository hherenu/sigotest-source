using Microsoft.EntityFrameworkCore;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Services;
using SIGO.Services.Validaciones;

namespace SIGO.Tests;

/// <summary>
/// Ciclo de vida del certificado (guardar borrador, cerrar con snapshot, invariantes
/// de orden) contra LocalDB real, con las transacciones Serializable de producción.
/// </summary>
public class CertificadoServiceTests(LocalDbFixture fx) : IClassFixture<LocalDbFixture>
{
    private CertificadoService Servicio(params string[] roles) =>
        new(new TestDbFactory(fx.Options), new FakeCurrentUser(roles));

    private sealed record Escenario(int ObraId, int EstructuraId, int ItemCantId, int ItemGlobalId);

    /// <summary>
    /// Obra + estructura con un agrupador y dos hojas: una con base de cantidad
    /// (100 un × $10 = $1.000) y una global (solo monto $5.000).
    /// </summary>
    private async Task<Escenario> CrearEstructuraAsync()
    {
        await using var db = fx.CrearContexto();
        var obra = new Obra { Nombre = $"Obra cert {Guid.NewGuid():N}", NumeroLicitacion = "LP CERT" };
        db.Obras.Add(obra);
        await db.SaveChangesAsync();

        var est = new EstructuraCostos { ObraId = obra.Id, Nombre = "Básico", Tipo = TipoEstructura.Basico };
        db.EstructurasCostos.Add(est);
        await db.SaveChangesAsync();

        var agrupador = new ItemEstructura { EstructuraCostosId = est.Id, EsAgrupador = true, Orden = 1, Codigo = "1", Descripcion = "Rubro" };
        db.ItemsEstructura.Add(agrupador);
        await db.SaveChangesAsync();

        var itemCant = new ItemEstructura
        {
            EstructuraCostosId = est.Id, Orden = 2, Codigo = "1.1", Descripcion = "Ítem con cantidad",
            AgrupadorPadreId = agrupador.Id, Cantidad = 100m, PUBasico = 10m, Monto = 1000m
        };
        var itemGlobal = new ItemEstructura
        {
            EstructuraCostosId = est.Id, Orden = 3, Codigo = "1.2", Descripcion = "Ítem global",
            AgrupadorPadreId = agrupador.Id, Monto = 5000m
        };
        db.ItemsEstructura.AddRange(itemCant, itemGlobal);
        await db.SaveChangesAsync();

        return new Escenario(obra.Id, est.Id, itemCant.Id, itemGlobal.Id);
    }

    /// <summary>RowVersion actual del certificado (el token que tendría una página recién abierta).</summary>
    private async Task<byte[]> RowVersionAsync(int certId)
    {
        await using var db = fx.CrearContexto();
        return (await db.Certificados.AsNoTracking().SingleAsync(c => c.Id == certId)).RowVersion;
    }

    /// <summary>Certificado en borrador con su bloque sobre la estructura.</summary>
    private async Task<(int CertId, int BloqueId)> CrearCertificadoAsync(Escenario e, int numero)
    {
        await using var db = fx.CrearContexto();
        var cert = new Certificado
        {
            ObraId = e.ObraId, Numero = numero, Mes = numero, Anio = 2030,
            FechaEmision = new DateTime(2030, numero, 1), Estado = EstadoCertificado.Borrador
        };
        db.Certificados.Add(cert);
        await db.SaveChangesAsync();

        var bloque = new CertificadoEstructura { CertificadoId = cert.Id, EstructuraCostosId = e.EstructuraId, Orden = 1 };
        db.CertificadoEstructuras.Add(bloque);
        await db.SaveChangesAsync();
        return (cert.Id, bloque.Id);
    }

    [Fact]
    public async Task GuardarItems_CalculaCantidadYMontoSegunLaBase()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);

        await Servicio(Roles.CCyR).GuardarItemsAsync(certId, new()
        {
            [bloqueId] = new() { [e.ItemCantId] = 10m, [e.ItemGlobalId] = 20m }
        });

        await using var db = fx.CrearContexto();
        var filas = await db.ItemsCertificado.Where(i => i.CertificadoEstructuraId == bloqueId).ToListAsync();

        var conCantidad = filas.Single(f => f.ItemEstructuraId == e.ItemCantId);
        Assert.Equal(10m, conCantidad.CantidadActual);   // 10% de 100 un
        Assert.Equal(100m, conCantidad.MontoActual);     // 10 un × $10
        Assert.Equal(10m, conCantidad.PorcentajeActual);

        var global = filas.Single(f => f.ItemEstructuraId == e.ItemGlobalId);
        Assert.Equal(0m, global.CantidadActual);         // sin base de cantidad
        Assert.Equal(1000m, global.MontoActual);         // 20% de $5.000
    }

    [Fact]
    public async Task Cerrar_CongelaSnapshotContratoYSubtotales()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.Admin);

        await servicio.GuardarItemsAsync(certId, new()
        {
            [bloqueId] = new() { [e.ItemCantId] = 10m, [e.ItemGlobalId] = 20m }
        });
        await servicio.CerrarAsync(certId);

        await using var db = fx.CrearContexto();
        var cert = await db.Certificados.Include(c => c.Estructuras).ThenInclude(b => b.Items)
            .SingleAsync(c => c.Id == certId);

        Assert.Equal(EstadoCertificado.Cerrado, cert.Estado);
        Assert.NotNull(cert.FechaCierre);

        var bloque = cert.Estructuras.Single();
        Assert.Equal(0m, bloque.SubtotalAnterior);       // primer certificado: sin anteriores
        Assert.Equal(1100m, bloque.SubtotalActual);      // 100 + 1000
        Assert.Equal(1100m, bloque.SubtotalAcumulado);

        var conCantidad = bloque.Items.Single(i => i.ItemEstructuraId == e.ItemCantId);
        Assert.Equal(0m, conCantidad.PorcentajeAnterior);
        Assert.Equal(10m, conCantidad.PorcentajeAcumulado);
        Assert.Equal(100m, conCantidad.MontoAcumulado);
        // Contrato congelado: editar la estructura después no altera el cerrado.
        Assert.Equal(100m, conCantidad.CantidadContrato);
        Assert.Equal(10m, conCantidad.PUBasico);
        Assert.Equal(1000m, conCantidad.MontoContrato);

        var global = bloque.Items.Single(i => i.ItemEstructuraId == e.ItemGlobalId);
        Assert.Equal(20m, global.PorcentajeAcumulado);   // por monto: 1000/5000
        Assert.Equal(5000m, global.MontoContrato);
    }

    [Fact]
    public async Task Cerrar_ConAnteriorEnBorrador_Rechaza()
    {
        var e = await CrearEstructuraAsync();
        await CrearCertificadoAsync(e, 1); // N°1 queda en borrador
        var (cert2Id, bloque2Id) = await CrearCertificadoAsync(e, 2);
        var servicio = Servicio(Roles.Admin);
        await servicio.GuardarItemsAsync(cert2Id, new() { [bloque2Id] = new() { [e.ItemCantId] = 5m } });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.CerrarAsync(cert2Id));
        Assert.Contains("en orden", ex.Message);
    }

    [Fact]
    public async Task Reabrir_ConPosteriorCerrado_Rechaza()
    {
        var e = await CrearEstructuraAsync();
        var (cert1Id, bloque1Id) = await CrearCertificadoAsync(e, 1);
        var (cert2Id, bloque2Id) = await CrearCertificadoAsync(e, 2);
        var servicio = Servicio(Roles.Admin);

        await servicio.GuardarItemsAsync(cert1Id, new() { [bloque1Id] = new() { [e.ItemCantId] = 10m } });
        await servicio.CerrarAsync(cert1Id);
        await servicio.GuardarItemsAsync(cert2Id, new() { [bloque2Id] = new() { [e.ItemCantId] = 5m } });
        await servicio.CerrarAsync(cert2Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ReabrirAsync(cert1Id));
        Assert.Contains("posteriores", ex.Message);
    }

    [Fact]
    public async Task Cerrar_AcumulaLosAnterioresCerrados()
    {
        var e = await CrearEstructuraAsync();
        var (cert1Id, bloque1Id) = await CrearCertificadoAsync(e, 1);
        var (cert2Id, bloque2Id) = await CrearCertificadoAsync(e, 2);
        var servicio = Servicio(Roles.Admin);

        await servicio.GuardarItemsAsync(cert1Id, new() { [bloque1Id] = new() { [e.ItemCantId] = 10m } });
        await servicio.CerrarAsync(cert1Id);
        await servicio.GuardarItemsAsync(cert2Id, new() { [bloque2Id] = new() { [e.ItemCantId] = 15m } });
        await servicio.CerrarAsync(cert2Id);

        await using var db = fx.CrearContexto();
        var fila = await db.ItemsCertificado
            .SingleAsync(i => i.CertificadoEstructuraId == bloque2Id && i.ItemEstructuraId == e.ItemCantId);

        Assert.Equal(10m, fila.PorcentajeAnterior);      // lo cerrado en el N°1
        Assert.Equal(100m, fila.MontoAnterior);
        Assert.Equal(25m, fila.PorcentajeAcumulado);     // 10% + 15%
        Assert.Equal(250m, fila.MontoAcumulado);

        var bloque = await db.CertificadoEstructuras.SingleAsync(b => b.Id == bloque2Id);
        Assert.Equal(100m, bloque.SubtotalAnterior);
        Assert.Equal(150m, bloque.SubtotalActual);
        Assert.Equal(250m, bloque.SubtotalAcumulado);
    }

    [Fact]
    public async Task BuildVm_PropagaSumasALosAgrupadores()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.Admin);
        await servicio.GuardarItemsAsync(certId, new()
        {
            [bloqueId] = new() { [e.ItemCantId] = 10m, [e.ItemGlobalId] = 20m }
        });

        var vm = (await servicio.BuildVmAsync(certId))!;

        var agrupador = vm.Bloques.Single().Items.Single(i => i.EsAgrupador);
        Assert.Equal(1100m, agrupador.MontoMes);          // 100 + 1000 de sus hojas
        Assert.Equal(6000m, agrupador.MontoContrato);     // 1000 + 5000
        Assert.Equal(0m, agrupador.MontoAnterior);
    }

    [Fact]
    public async Task BuildVm_DeCerrado_UsaElSnapshotAunqueLaEstructuraCambie()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.Admin);
        await servicio.GuardarItemsAsync(certId, new() { [bloqueId] = new() { [e.ItemCantId] = 10m } });
        await servicio.CerrarAsync(certId);

        // Edición retroactiva del contrato DESPUÉS del cierre.
        await using (var db = fx.CrearContexto())
        {
            var item = await db.ItemsEstructura.SingleAsync(i => i.Id == e.ItemCantId);
            item.Cantidad = 999m; item.PUBasico = 999m; item.Monto = 999999m;
            await db.SaveChangesAsync();
        }

        var vm = (await servicio.BuildVmAsync(certId))!;

        var fila = vm.Bloques.Single().Items.Single(i => i.ItemEstructuraId == e.ItemCantId);
        // El certificado cerrado muestra el contrato congelado, no el editado.
        Assert.Equal(100m, fila.CantidadContrato);
        Assert.Equal(10m, fila.PUBasico);
        Assert.Equal(1000m, fila.MontoContrato);
    }

    [Fact]
    public async Task Cerrar_CongelaTambienLosItemsSinMovimiento()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.Admin);
        // Solo se certifica el ítem con cantidad; el global queda sin movimiento.
        await servicio.GuardarItemsAsync(certId, new() { [bloqueId] = new() { [e.ItemCantId] = 10m } });
        await servicio.CerrarAsync(certId);

        // Edición retroactiva de un ítem NUNCA certificado.
        await using (var db = fx.CrearContexto())
        {
            var item = await db.ItemsEstructura.SingleAsync(i => i.Id == e.ItemGlobalId);
            item.Monto = 999999m;
            await db.SaveChangesAsync();
        }

        var vm = (await servicio.BuildVmAsync(certId))!;

        var bloque = vm.Bloques.Single();
        var fila = bloque.Items.Single(i => i.ItemEstructuraId == e.ItemGlobalId);
        // Sin fila congelada, el cerrado mostraba el contrato vivo ($999.999) y el
        // total y el % de avance del certificado emitido cambiaban retroactivamente.
        Assert.Equal(5000m, fila.MontoContrato);
        Assert.Equal(6000m, bloque.TotalMontoContrato);   // 1000 + 5000
    }

    [Fact]
    public async Task BuildVm_DeCerrado_ExcluyeItemsAgregadosDespuesDelCierre()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.Admin);
        await servicio.GuardarItemsAsync(certId, new() { [bloqueId] = new() { [e.ItemCantId] = 10m } });
        await servicio.CerrarAsync(certId);

        int itemNuevoId;
        await using (var db = fx.CrearContexto())
        {
            var nuevo = new ItemEstructura
            {
                EstructuraCostosId = e.EstructuraId, Orden = 4, Codigo = "1.3",
                Descripcion = "Agregado después del cierre", Monto = 7000m
            };
            db.ItemsEstructura.Add(nuevo);
            await db.SaveChangesAsync();
            itemNuevoId = nuevo.Id;
        }

        var cerrado = (await servicio.BuildVmAsync(certId))!;
        // No forma parte del certificado emitido: ni el renglón ni su contrato en el total.
        Assert.DoesNotContain(cerrado.Bloques.Single().Items, i => i.ItemEstructuraId == itemNuevoId);
        Assert.Equal(6000m, cerrado.Bloques.Single().TotalMontoContrato);

        // Reabierto (borrador) vuelve a mostrar la estructura viva, ítem nuevo incluido.
        await servicio.ReabrirAsync(certId);
        var borrador = (await servicio.BuildVmAsync(certId))!;
        Assert.Contains(borrador.Bloques.Single().Items, i => i.ItemEstructuraId == itemNuevoId);
    }

    [Fact]
    public async Task Reabrir_EliminaLasFilasAutoGeneradasSinMovimiento()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.Admin);
        await servicio.GuardarItemsAsync(certId, new() { [bloqueId] = new() { [e.ItemCantId] = 10m } });
        await servicio.CerrarAsync(certId);

        await using (var db = fx.CrearContexto())
        {
            // El cierre congeló una fila para cada hoja, también la sin movimiento.
            var filas = await db.ItemsCertificado.Where(i => i.CertificadoEstructuraId == bloqueId).ToListAsync();
            Assert.Equal(2, filas.Count);
            var global = filas.Single(f => f.ItemEstructuraId == e.ItemGlobalId);
            Assert.Equal(0m, global.MontoActual);
            Assert.Equal(5000m, global.MontoContrato);
        }

        await servicio.ReabrirAsync(certId);

        await using (var db2 = fx.CrearContexto())
        {
            // La reapertura limpia la fila auto-generada; la carga del usuario sobrevive.
            var filas = await db2.ItemsCertificado.Where(i => i.CertificadoEstructuraId == bloqueId).ToListAsync();
            Assert.Equal(e.ItemCantId, Assert.Single(filas).ItemEstructuraId);
        }
    }

    [Fact]
    public async Task Reabrir_VuelveABorradorYLimpiaElCierre()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.Admin);
        await servicio.GuardarItemsAsync(certId, new() { [bloqueId] = new() { [e.ItemCantId] = 10m } });
        await servicio.CerrarAsync(certId);

        await servicio.ReabrirAsync(certId);

        await using var db = fx.CrearContexto();
        var cert = await db.Certificados.SingleAsync(c => c.Id == certId);
        Assert.Equal(EstadoCertificado.Borrador, cert.Estado);
        Assert.Null(cert.FechaCierre);
    }

    [Fact]
    public async Task Eliminar_UnCerrado_Rechaza()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.Admin);
        await servicio.GuardarItemsAsync(certId, new() { [bloqueId] = new() { [e.ItemCantId] = 10m } });
        await servicio.CerrarAsync(certId);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await servicio.EliminarAsync(certId, await RowVersionAsync(certId)));
    }

    [Fact]
    public async Task Eliminar_Borrador_ConElTokenDelListado_Elimina()
    {
        var e = await CrearEstructuraAsync();
        var (certId, _) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.CCyR);

        Assert.True(await servicio.EliminarAsync(certId, await RowVersionAsync(certId)));
        Assert.False(await servicio.EliminarAsync(certId, []));   // ya no existe: patrón bool-false

        await using var db = fx.CrearContexto();
        Assert.False(await db.Certificados.AnyAsync(c => c.Id == certId));
    }

    [Fact]
    public async Task Eliminar_SiOtroUsuarioGuardoDesdeElListado_ConflictoYNoBorra()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.Admin);
        var tokenDelListado = await RowVersionAsync(certId);

        // Otro usuario carga porcentajes después de que el listado se mostró.
        await servicio.GuardarItemsAsync(certId, new() { [bloqueId] = new() { [e.ItemCantId] = 10m } });

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => servicio.EliminarAsync(certId, tokenDelListado));

        await using var db = fx.CrearContexto();
        Assert.True(await db.ItemsCertificado.AnyAsync(i => i.CertificadoEstructuraId == bloqueId));
    }

    // ── Contrato editado con el certificado en borrador ─────────────────────────

    /// <summary>Otro usuario cambia el contrato del ítem con cantidad: 100 → 200 un (monto 1.000 → 2.000).</summary>
    private async Task DuplicarCantidadDelContratoAsync(Escenario e)
    {
        await using var db = fx.CrearContexto();
        var item = await db.ItemsEstructura.SingleAsync(i => i.Id == e.ItemCantId);
        item.Cantidad = 200m;
        item.Monto = 2000m;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Cerrar_TrasEditarElContratoDelItem_CongelaElMovimientoDelContratoVigente()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.Admin);
        await servicio.GuardarItemsAsync(certId, new() { [bloqueId] = new() { [e.ItemCantId] = 10m } });

        await DuplicarCantidadDelContratoAsync(e);
        await servicio.CerrarAsync(certId);

        await using var db = fx.CrearContexto();
        var fila = await db.ItemsCertificado
            .SingleAsync(i => i.CertificadoEstructuraId == bloqueId && i.ItemEstructuraId == e.ItemCantId);
        // Antes: cantidad 10 y monto 100 (contrato viejo) junto a un contrato de 200 un:
        // % del mes 10 y % acumulado 5 en el primer certificado.
        Assert.Equal(10m, fila.PorcentajeActual);
        Assert.Equal(20m, fila.CantidadActual);
        Assert.Equal(200m, fila.MontoActual);
        Assert.Equal(10m, fila.PorcentajeAcumulado);
        Assert.Equal(200m, fila.CantidadContrato);
        Assert.Equal(2000m, fila.MontoContrato);
        Assert.Equal(200m, (await db.CertificadoEstructuras.SingleAsync(b => b.Id == bloqueId)).SubtotalActual);
    }

    [Fact]
    public async Task BuildVm_DeBorrador_MuestraElMovimientoConElContratoVigente()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.Admin);
        await servicio.GuardarItemsAsync(certId, new() { [bloqueId] = new() { [e.ItemCantId] = 10m } });

        await DuplicarCantidadDelContratoAsync(e);
        var vm = (await servicio.BuildVmAsync(certId))!;

        // Lo mismo que la grilla de carga calcula en vivo y que el cierre va a congelar.
        var fila = vm.Bloques.Single().Items.Single(i => i.ItemEstructuraId == e.ItemCantId);
        Assert.Equal(10m, fila.PorcentajeMes);
        Assert.Equal(20m, fila.CantidadMes);
        Assert.Equal(200m, fila.MontoMes);
        Assert.Equal(10m, fila.PorcentajeAcumulado);
    }

    // ── Alta, guardado, bloques y transiciones ───────────────────────────────────

    [Theory]
    [InlineData(0, 5, 2030, "número")]
    [InlineData(1, 13, 2030, "mes")]
    [InlineData(1, 0, 2030, "mes")]
    [InlineData(1, 5, 2009, "año")]
    [InlineData(1, 5, 2100, "año")]
    public async Task Crear_ConNumeroMesOAnioFueraDeRango_Rechaza(int numero, int mes, int anio, string campo)
    {
        var e = await CrearEstructuraAsync();

        var (id, rechazo) = await Servicio(Roles.CCyR).CrearAsync(e.ObraId, new Models.ViewModels.CertificadoNuevoVM
        {
            Numero = numero, Mes = mes, Anio = anio, EstructuraCostosId = e.EstructuraId, FechaEmision = new DateTime(2030, 1, 1)
        });

        Assert.Null(id);
        Assert.Contains(campo, rechazo, StringComparison.OrdinalIgnoreCase);
        await using var db = fx.CrearContexto();
        Assert.False(await db.Certificados.AnyAsync(c => c.ObraId == e.ObraId));
    }

    [Fact]
    public async Task Crear_ConElPrimerAnioAdmitido_Crea()
    {
        // 2010 es el primer año de certificado admitido (obras con certificación histórica).
        var e = await CrearEstructuraAsync();

        var (id, rechazo) = await Servicio(Roles.CCyR).CrearAsync(e.ObraId, new Models.ViewModels.CertificadoNuevoVM
        {
            Numero = 1, Mes = 1, Anio = 2010, EstructuraCostosId = e.EstructuraId, FechaEmision = new DateTime(2010, 1, 31)
        });

        Assert.Null(rechazo);
        Assert.NotNull(id);
    }

    [Fact]
    public async Task Crear_SinFechaDeEmision_Rechaza()
    {
        // Un selector de fecha vaciado deja el valor por defecto (01/01/0001).
        var e = await CrearEstructuraAsync();

        var (id, rechazo) = await Servicio(Roles.CCyR).CrearAsync(e.ObraId, new Models.ViewModels.CertificadoNuevoVM
        {
            Numero = 1, Mes = 5, Anio = 2030, EstructuraCostosId = e.EstructuraId, FechaEmision = default
        });

        Assert.Null(id);
        Assert.Contains("fecha de emisión", rechazo);
    }

    [Fact]
    public async Task Crear_ValidoArmaElBorradorConSuPrimerBloque()
    {
        var e = await CrearEstructuraAsync();

        var (id, rechazo) = await Servicio(Roles.CCyR).CrearAsync(e.ObraId, new Models.ViewModels.CertificadoNuevoVM
        {
            Numero = 1, Mes = 5, Anio = 2030, EstructuraCostosId = e.EstructuraId, FechaEmision = new DateTime(2030, 5, 31)
        });

        Assert.Null(rechazo);
        await using var db = fx.CrearContexto();
        var cert = await db.Certificados.Include(c => c.Estructuras).SingleAsync(c => c.Id == id);
        Assert.Equal(EstadoCertificado.Borrador, cert.Estado);
        Assert.Equal(e.EstructuraId, Assert.Single(cert.Estructuras).EstructuraCostosId);
    }

    [Fact]
    public async Task GuardarItems_ConservaObservacionesYTipoMovimiento_YDescartaLoQueNoEsCarga()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.CCyR);
        await servicio.GuardarItemsAsync(certId, new() { [bloqueId] = new() { [e.ItemCantId] = 10m, [e.ItemGlobalId] = 20m } });

        await using (var db = fx.CrearContexto())
        {
            // Lo que no viene del editor: observaciones y override del tipo de movimiento.
            var fila = await db.ItemsCertificado.SingleAsync(i => i.CertificadoEstructuraId == bloqueId && i.ItemEstructuraId == e.ItemCantId);
            fila.Observaciones = "Medición parcial";
            fila.TipoMovimiento = TipoMovimiento.Demasia;
            await db.SaveChangesAsync();
        }

        int agrupadorId;
        await using (var db = fx.CrearContexto())
            agrupadorId = (await db.ItemsEstructura.SingleAsync(i => i.EstructuraCostosId == e.EstructuraId && i.EsAgrupador)).Id;

        // 0,00001 % se guarda con 4 decimales como 0: no es carga y su fila se borra. El
        // agrupador y un ítem ajeno a la estructura se ignoran.
        await servicio.GuardarItemsAsync(certId, new()
        {
            [bloqueId] = new() { [e.ItemCantId] = 15m, [e.ItemGlobalId] = 0.00001m, [agrupadorId] = 50m, [int.MaxValue] = 50m }
        });

        await using var db2 = fx.CrearContexto();
        var filas = await db2.ItemsCertificado.Where(i => i.CertificadoEstructuraId == bloqueId).ToListAsync();
        var unica = Assert.Single(filas);
        Assert.Equal(e.ItemCantId, unica.ItemEstructuraId);
        Assert.Equal(15m, unica.PorcentajeActual);
        Assert.Equal("Medición parcial", unica.Observaciones);
        Assert.Equal(TipoMovimiento.Demasia, unica.TipoMovimiento);
    }

    [Fact]
    public async Task Bloques_DuplicadoYEnCerrado_SeRechazanConSuMotivo()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.CCyR);

        var duplicado = await servicio.AgregarBloqueAsync(certId, e.EstructuraId);
        Assert.Equal(MotivoRechazoBloque.Duplicado, duplicado.Rechazo?.Motivo);

        await servicio.CerrarAsync(certId);
        var quitar = await servicio.QuitarBloqueAsync(bloqueId);
        Assert.Equal(MotivoRechazoBloque.NoEditable, quitar.Rechazo?.Motivo);

        await using var db = fx.CrearContexto();
        Assert.Equal(1, await db.CertificadoEstructuras.CountAsync(b => b.CertificadoId == certId));
    }

    [Fact]
    public async Task AprobarYVolverACerrado_SoloDesdeElEstadoQueCorresponde()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.Admin);
        await servicio.GuardarItemsAsync(certId, new() { [bloqueId] = new() { [e.ItemCantId] = 10m } });

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.AprobarAsync(certId));   // borrador

        await servicio.CerrarAsync(certId);
        await servicio.AprobarAsync(certId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ReabrirAsync(certId));   // aprobado
        await servicio.DesaprobarAsync(certId);

        await using var db = fx.CrearContexto();
        Assert.Equal(EstadoCertificado.Cerrado, (await db.Certificados.SingleAsync(c => c.Id == certId)).Estado);
    }

    [Fact]
    public async Task GuardarItems_ConTokenDeSesionViejo_DaConflicto()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var servicio = Servicio(Roles.Admin);

        byte[] tokenViejo;
        await using (var db = fx.CrearContexto())
            tokenViejo = (await db.Certificados.SingleAsync(c => c.Id == certId)).RowVersion;

        // Otro guardado en el medio bumpea el rowversion del certificado.
        await servicio.GuardarItemsAsync(certId, new() { [bloqueId] = new() { [e.ItemCantId] = 5m } });

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            servicio.GuardarItemsAsync(certId, new() { [bloqueId] = new() { [e.ItemCantId] = 7m } }, tokenViejo));
    }

    [Fact]
    public async Task Mutaciones_SinRolDelModulo_Rechazan()
    {
        var e = await CrearEstructuraAsync();
        var (certId, bloqueId) = await CrearCertificadoAsync(e, 1);
        var sinRol = Servicio(Roles.Director); // rol de otro módulo

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sinRol.GuardarItemsAsync(certId, new() { [bloqueId] = new() { [e.ItemCantId] = 10m } }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sinRol.CerrarAsync(certId));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await sinRol.EliminarAsync(certId, await RowVersionAsync(certId)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sinRol.CrearAsync(e.ObraId, new Models.ViewModels.CertificadoNuevoVM()));
    }
}
