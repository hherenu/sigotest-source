using Microsoft.EntityFrameworkCore;
using SIGO.Data;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Models.ViewModels;
using SIGO.Services.Validaciones;

namespace SIGO.Services;

public class RedeterminacionService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser)
{
    /// <summary>Defensa en profundidad (ver <see cref="Roles.Exigir"/>): módulo Certificaciones.</summary>
    private void ExigirRol(string accion) => Roles.Exigir(currentUser.IsInRole, Roles.Certificaciones, accion);

    public async Task<RedeterminacionVM> CalcularAsync(
        int obraId, int tablaPonderacionId,
        int anioBase, int mesBase, string? idPublicacionBase,
        int anioSalto, int mesSalto, string? idPublicacionSalto)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // EntidadNoEncontradaException (no ArgumentException, que Persistencia NO captura
        // y tumbaba el estado del circuito si el id venía viejo de la URL).
        var obra = await db.ResumenObraAsync(obraId)
            ?? throw new EntidadNoEncontradaException("Obra no encontrada.");

        // Calcular es 100% lectura (arma un VM): sin tracking.
        var tabla = await db.TablasPonderacion.AsNoTracking()
            .Include(t => t.Items)
                .ThenInclude(i => i.Indice)
            .FirstOrThrowAsync(t => t.Id == tablaPonderacionId && t.ObraId == obraId, "Tabla no encontrada.");

        Validacion.Exigir(RedeterminacionValidator.Calcular(tabla.Items.Sum(i => i.PesoPorcentaje)));

        var indiceIds = tabla.Items.Select(i => i.IndiceId).Distinct().ToList();

        var valores = await db.ValoresIndice.AsNoTracking()
            .Where(v => indiceIds.Contains(v.IndiceId)
                && ((v.Anio == anioBase  && v.Mes == mesBase  && (idPublicacionBase  == null || v.IdPublicacion == idPublicacionBase  || v.IdPublicacion == null))
                 || (v.Anio == anioSalto && v.Mes == mesSalto && (idPublicacionSalto == null || v.IdPublicacion == idPublicacionSalto || v.IdPublicacion == null))))
            .ToListAsync();

        // Selección determinística cuando un mes tiene más de una fila candidata:
        // 1) la de la publicación pedida, 2) la fila sin publicación (tasas BN),
        // 3) la publicación más reciente. Sin este orden, FirstOrDefault dependía
        // del orden arbitrario que devolviera SQL Server.
        // El desempate 3 ordena por el PERÍODO de la publicación (PublicacionIndec.Orden),
        // no por su texto: el sufijo es "MM_AA" y alfabéticamente 12_26 gana a 01_27.
        ValorIndice? Pick(int indiceId, int anio, int mes, string? idPublicacion) => valores
            .Where(v => v.IndiceId == indiceId && v.Anio == anio && v.Mes == mes
                     && (idPublicacion == null || v.IdPublicacion == idPublicacion || v.IdPublicacion == null))
            .OrderByDescending(v => idPublicacion != null && v.IdPublicacion == idPublicacion)
            .ThenByDescending(v => v.IdPublicacion == null)
            .ThenByDescending(v => PublicacionIndec.Orden(v.IdPublicacion))
            .FirstOrDefault();

        var items = tabla.Items.OrderBy(i => i.Numero).Select(item =>
        {
            var vBase  = Pick(item.IndiceId, anioBase,  mesBase,  idPublicacionBase);
            var vSalto = Pick(item.IndiceId, anioSalto, mesSalto, idPublicacionSalto);

            // Ambos valores deben ser > 0: la base como divisor, y el salto porque un
            // coeficiente 0 (dato inválido) entraría a la cadena de VariacionAcumulada.
            decimal? kiK0 = (vBase?.Valor > 0 && vSalto?.Valor > 0)
                ? Math.Round(vSalto.Valor / vBase.Valor, 6)
                : null;

            decimal? variacion = (kiK0.HasValue)
                ? Math.Round(kiK0.Value * (item.PesoPorcentaje / 100m), 6)
                : null;

            return new ItemCalculadoVM
            {
                Numero             = item.Numero,
                Insumo             = item.Insumo,
                PesoPorcentaje     = item.PesoPorcentaje,
                ValorMesBase       = vBase?.Valor,
                ValorMesSalto      = vSalto?.Valor,
                KiK0               = kiK0,
                VariacionPonderada = variacion,
                DescripcionINDEC   = item.DescripcionINDEC,
                IndiceId           = item.IndiceId,
                ItemPonderacionId  = item.Id,
                ValorIndiceBaseId  = vBase?.Id,
                ValorIndiceSaltoId = vSalto?.Id
            };
        }).ToList();

        decimal? totalKiK0 = items.Any(i => i.VariacionPonderada == null)
            ? null
            : Math.Round(items.Sum(i => i.VariacionPonderada!.Value), 6);

        return new RedeterminacionVM
        {
            ObraId             = obra.Id,
            ObraNombre         = obra.Nombre,
            TablaPonderacionId = tabla.Id,
            Items           = items,
            AnioBase             = anioBase,
            MesBase              = mesBase,
            IdPublicacionBase    = idPublicacionBase,
            AnioSalto            = anioSalto,
            MesSalto             = mesSalto,
            IdPublicacionSalto   = idPublicacionSalto,
            TotalKiK0       = totalKiK0,
            PorcentajeAumento = totalKiK0.HasValue
                ? Math.Round((totalKiK0.Value - 1) * 100, 4)
                : null,
            Calculado       = true
        };
    }

    /// <summary>
    /// Guarda el cálculo mostrado como nuevo disparo de la obra: asigna NroDisparo
    /// (Max+1) y encadena la VariacionAcumulada como producto de los coeficientes
    /// previos. (Lógica movida desde Calcular.razor — auditoría 2026-07-20, M12.)
    /// </summary>
    public async Task GuardarDisparoAsync(int obraId, int tablaPonderacionId, RedeterminacionVM vm)
    {
        ExigirRol("guardar el disparo");

        Validacion.Exigir(RedeterminacionValidator.GuardarDisparo(vm.TotalKiK0));

        await using var db = await dbFactory.CreateDbContextAsync();
        Validacion.Exigir(RedeterminacionValidator.TablaDeLaObra(await ObraDeTablaAsync(db, tablaPonderacionId), obraId));

        // Serializable: el Max+1 y el producto de coeficientes miran OTRAS filas de
        // la obra (el RowVersion propio no las protege) — sin esto, guardar mientras
        // otro usuario elimina el último disparo encadena un coeficiente ya borrado.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        // Max+1 (no Count+1): tras eliminar un disparo intermedio, Count reutilizaría
        // un número existente y chocaría con el índice único (ObraId, NroDisparo).
        var nroDisparo = await db.RedeterminacionesGuardadas
            .Where(r => r.ObraId == obraId)
            .MaxAsync(r => (int?)r.NroDisparo) ?? 0;
        nroDisparo++;

        var coefsPrevios = await db.RedeterminacionesGuardadas
            .Where(r => r.ObraId == obraId).OrderBy(r => r.NroDisparo).Select(r => r.TotalKiK0).ToListAsync();
        var varAcum = coefsPrevios.Where(k => k.HasValue).Aggregate(1m, (acc, k) => acc * k!.Value);
        varAcum *= vm.TotalKiK0!.Value; // no-null garantizado por el validator de arriba

        db.RedeterminacionesGuardadas.Add(new RedeterminacionGuardada
        {
            ObraId = obraId,
            TablaPonderacionId = tablaPonderacionId,
            Estado = EstadoRedeterminacion.Calculada,
            FechaGuardado = DateTime.UtcNow,
            NroDisparo = nroDisparo,
            MesBase = vm.MesBase,
            AnioBase = vm.AnioBase,
            IdPublicacionBase = vm.IdPublicacionBase,
            MesSalto = vm.MesSalto,
            AnioSalto = vm.AnioSalto,
            IdPublicacionSalto = vm.IdPublicacionSalto,
            TotalKiK0 = vm.TotalKiK0,
            PorcentajeAumento = vm.PorcentajeAumento,
            VariacionAcumulada = varAcum,
            Items = vm.Items.Select(MapItem).ToList()
        });

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>
    /// Reemplaza el cálculo de un disparo existente (solo Borrador/Calculada) y
    /// re-encadena la VariacionAcumulada de TODOS los disparos de la obra.
    /// Devuelve el ObraId del disparo para la navegación posterior.
    /// </summary>
    public async Task<int> RecalcularDisparoAsync(int disparoId, int obraId, int tablaPonderacionId, RedeterminacionVM vm)
    {
        ExigirRol("recalcular el disparo");

        Validacion.Exigir(RedeterminacionValidator.RecalcularDisparo(vm.TotalKiK0));

        await using var db = await dbFactory.CreateDbContextAsync();
        // Serializable: la recadena de VariacionAcumulada recorre TODOS los disparos
        // de la obra; un alta/borrado concurrente la dejaría inconsistente.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var existing = await db.RedeterminacionesGuardadas.Include(r => r.Items)
            .FirstOrThrowAsync(r => r.Id == disparoId,
                "El disparo a recalcular ya no existe (otro usuario pudo haberlo eliminado).");

        Validacion.Exigir(RedeterminacionValidator.AdmiteRecalculo(existing.Estado, existing.NroDisparo));
        Validacion.Exigir(RedeterminacionValidator.MismaObra(existing.ObraId, obraId));
        Validacion.Exigir(RedeterminacionValidator.TablaDeLaObra(await ObraDeTablaAsync(db, tablaPonderacionId), obraId));

        existing.TablaPonderacionId = tablaPonderacionId;
        existing.MesBase = vm.MesBase;
        existing.AnioBase = vm.AnioBase;
        existing.IdPublicacionBase = vm.IdPublicacionBase;
        existing.MesSalto = vm.MesSalto;
        existing.AnioSalto = vm.AnioSalto;
        existing.IdPublicacionSalto = vm.IdPublicacionSalto;
        existing.TotalKiK0 = vm.TotalKiK0;
        existing.PorcentajeAumento = vm.PorcentajeAumento;

        db.RemoveRange(existing.Items);
        existing.Items = vm.Items.Select(MapItem).ToList();

        var todosDisparos = await db.RedeterminacionesGuardadas
            .Where(r => r.ObraId == existing.ObraId).OrderBy(r => r.NroDisparo).ToListAsync();
        decimal acum = 1m;
        foreach (var d in todosDisparos)
        {
            if (d.TotalKiK0.HasValue) acum *= d.TotalKiK0.Value;
            d.VariacionAcumulada = d.TotalKiK0.HasValue ? acum : null;
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return existing.ObraId;
    }

    /// <summary>
    /// Elimina un disparo (solo Borrador/Calculada y solo el último de la obra: los
    /// posteriores acumulan su coeficiente). Devuelve false si ya no existía —
    /// eliminado por otro usuario— para que la página informe en vez de fallar.
    /// (Lógica movida desde RedeterminacionHistorial.razor: era la única página con
    /// transacción y reglas de negocio propias.)
    /// </summary>
    public async Task<bool> EliminarDisparoAsync(int disparoId, byte[] rowVersionListado)
    {
        ExigirRol("eliminar el disparo");
        await using var db = await dbFactory.CreateDbContextAsync();
        // Serializable: el chequeo de posteriores mira otras filas de la obra — sin
        // esto, eliminar mientras otro usuario guarda un disparo nuevo deja hueco en
        // la numeración y un coeficiente borrado dentro de su cadena.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var disparo = await db.RedeterminacionesGuardadas.FirstOrDefaultAsync(x => x.Id == disparoId);
        if (disparo is null) return false;

        var hayPosterior = await db.RedeterminacionesGuardadas
            .AnyAsync(x => x.ObraId == disparo.ObraId && x.NroDisparo > disparo.NroDisparo);
        Validacion.Exigir(RedeterminacionValidator.EliminarDisparo(disparo.Estado, disparo.NroDisparo, hayPosterior));

        // El token del listado viaja al DELETE (detecta ediciones concurrentes desde
        // que la página lo mostró).
        db.Entry(disparo).Property(x => x.RowVersion).OriginalValue = rowVersionListado;

        db.RedeterminacionesGuardadas.Remove(disparo);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return true;
    }

    /// <summary>
    /// Datos administrativos de un disparo para la página Admin (cabecera solo lectura
    /// + campos editables + RowVersion como token de la sesión). Null si no existe.
    /// </summary>
    public async Task<RedeterminacionAdminVM?> BuildAdminVmAsync(int disparoId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        return await db.RedeterminacionesGuardadas.AsNoTracking()
            .Where(r => r.Id == disparoId)
            .Select(r => new RedeterminacionAdminVM
            {
                Id = r.Id,
                NroDisparo = r.NroDisparo,
                ObraId = r.ObraId,
                ObraNombre = r.Obra.Nombre,
                MesSalto = r.MesSalto,
                AnioSalto = r.AnioSalto,
                PorcentajeAumento = r.PorcentajeAumento,
                NroExpedienteVR = r.NroExpedienteVR,
                FechaAprobacionCCyR = r.FechaAprobacionCCyR,
                FechaAprobacionOS = r.FechaAprobacionOS,
                FechaLimitePresentacion = r.FechaLimitePresentacion,
                RowVersion = r.RowVersion
            })
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Guarda SOLO los campos administrativos de un disparo (expediente y fechas de
    /// aprobación): el cálculo, el estado y la cadena de variación no se tocan. El
    /// RowVersion de la sesión viaja como token: una edición concurrente desde que la
    /// página cargó termina en conflicto de concurrencia, no en un lost update.
    /// </summary>
    public async Task GuardarDatosAdminAsync(int disparoId, RedeterminacionAdminVM vm, byte[]? rowVersionSesion = null)
    {
        ExigirRol("guardar los datos administrativos");
        await using var db = await dbFactory.CreateDbContextAsync();

        var r = await db.RedeterminacionesGuardadas.FirstOrThrowAsync(x => x.Id == disparoId,
            "El disparo ya no existe (otro usuario pudo haberlo eliminado).");

        if (rowVersionSesion is not null)
            db.Entry(r).Property(x => x.RowVersion).OriginalValue = rowVersionSesion;

        r.NroExpedienteVR = vm.NroExpedienteVR;
        r.FechaAprobacionCCyR = vm.FechaAprobacionCCyR;
        r.FechaAprobacionOS = vm.FechaAprobacionOS;
        r.FechaLimitePresentacion = vm.FechaLimitePresentacion;

        await db.SaveChangesAsync();
    }

    /// <summary>ObraId de una tabla de ponderación (null si la tabla no existe) — para TablaDeLaObra.</summary>
    private static Task<int?> ObraDeTablaAsync(AppDbContext db, int tablaPonderacionId) =>
        db.TablasPonderacion.Where(t => t.Id == tablaPonderacionId)
            .Select(t => (int?)t.ObraId).FirstOrDefaultAsync();

    private static RedeterminacionGuardadaItem MapItem(ItemCalculadoVM i) => new()
    {
        Numero = i.Numero,
        Insumo = i.Insumo,
        PesoPorcentaje = i.PesoPorcentaje,
        ValorMesBase = i.ValorMesBase,
        ValorMesSalto = i.ValorMesSalto,
        KiK0 = i.KiK0,
        VariacionPonderada = i.VariacionPonderada,
        DescripcionINDEC = i.DescripcionINDEC,
        // Trazabilidad: de qué registros exactos salió cada valor del cálculo.
        ItemPonderacionId = i.ItemPonderacionId,
        IndiceId = i.IndiceId,
        ValorIndiceBaseId = i.ValorIndiceBaseId,
        ValorIndiceSaltoId = i.ValorIndiceSaltoId
    };

    /// <summary>
    /// Obras para el selector de Calcular y el filtro del historial, ordenadas por
    /// nombre. Incluye los datos que la ficha de obra de Calcular muestra.
    /// </summary>
    public async Task<List<ObraOpcionVM>> GetObrasAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        return await db.Obras.AsNoTracking()
            .OrderBy(o => o.Nombre)
            .Select(o => new ObraOpcionVM
            {
                Id = o.Id,
                Nombre = o.Nombre,
                NumeroLicitacion = o.NumeroLicitacion,
                Contratista = o.Contratista,
                FechaActaInicio = o.FechaActaInicio,
                FechaFinalContrato = o.FechaFinalContrato
            })
            .ToListAsync();
    }

    /// <summary>Tablas de ponderación de una obra (Calcular usa la primera).</summary>
    public async Task<List<TablaOpcionVM>> GetTablasAsync(int obraId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        return await db.TablasPonderacion.AsNoTracking()
            .Where(t => t.ObraId == obraId)
            .OrderBy(t => t.Id)
            .Select(t => new TablaOpcionVM { Id = t.Id, Nombre = t.Nombre })
            .ToListAsync();
    }

    /// <summary>
    /// Historial de disparos guardados, con filtro opcional por obra, en el orden del
    /// listado (Obra.Nombre → NroDisparo). Cada fila lleva su RowVersion como token
    /// para la eliminación y los datos de la Obra para el agrupado.
    /// </summary>
    public async Task<List<RedeterminacionHistorialVM>> GetHistorialAsync(int? obraId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var query = db.RedeterminacionesGuardadas.AsNoTracking();
        if (obraId.HasValue)
            query = query.Where(r => r.ObraId == obraId.Value);

        return await query
            .OrderBy(r => r.Obra.Nombre).ThenBy(r => r.NroDisparo)
            .Select(r => new RedeterminacionHistorialVM
            {
                Id = r.Id,
                NroDisparo = r.NroDisparo,
                Estado = r.Estado,
                ObraId = r.ObraId,
                ObraNombre = r.Obra.Nombre,
                ObraNumeroLicitacion = r.Obra.NumeroLicitacion,
                MesSalto = r.MesSalto,
                AnioSalto = r.AnioSalto,
                IdPublicacionBase = r.IdPublicacionBase,
                IdPublicacionSalto = r.IdPublicacionSalto,
                TotalKiK0 = r.TotalKiK0,
                PorcentajeAumento = r.PorcentajeAumento,
                VariacionAcumulada = r.VariacionAcumulada,
                NroExpedienteVR = r.NroExpedienteVR,
                FechaAprobacionCCyR = r.FechaAprobacionCCyR,
                FechaAprobacionOS = r.FechaAprobacionOS,
                FechaLimitePresentacion = r.FechaLimitePresentacion,
                RowVersion = r.RowVersion
            })
            .ToListAsync();
    }

    /// <summary>
    /// Parámetros con los que se guardó un disparo, para que Calcular los precargue
    /// al entrar con ?editarId=. Null si el disparo ya no existe.
    /// </summary>
    public async Task<RedeterminacionParaRecalcularVM?> GetDisparoParaRecalcularAsync(int disparoId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        return await db.RedeterminacionesGuardadas.AsNoTracking()
            .Where(r => r.Id == disparoId)
            .Select(r => new RedeterminacionParaRecalcularVM
            {
                ObraId = r.ObraId,
                AnioBase = r.AnioBase,
                MesBase = r.MesBase,
                IdPublicacionBase = r.IdPublicacionBase,
                AnioSalto = r.AnioSalto,
                MesSalto = r.MesSalto
            })
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Mes salto del último disparo de la obra (null si no hay ninguno) — para el aviso
    /// de continuidad de Calcular: el producto de coeficientes de la VariacionAcumulada
    /// solo representa la variación real si cada tramo arranca donde saltó el anterior.
    /// </summary>
    public async Task<(int Anio, int Mes)?> UltimoSaltoAsync(int obraId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var ultimo = await db.RedeterminacionesGuardadas.AsNoTracking()
            .Where(r => r.ObraId == obraId)
            .OrderByDescending(r => r.NroDisparo)
            .Select(r => new { r.AnioSalto, r.MesSalto })
            .FirstOrDefaultAsync();
        return ultimo is null ? null : (ultimo.AnioSalto, ultimo.MesSalto);
    }

    /// <summary>
    /// Detalle de un cálculo guardado para la página Ver: cabecera + snapshot de
    /// ítems ordenados por Numero. Null si no existe.
    /// </summary>
    public async Task<RedeterminacionDetalleVM?> GetDetalleAsync(int disparoId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        return await db.RedeterminacionesGuardadas.AsNoTracking()
            .Where(r => r.Id == disparoId)
            .Select(r => new RedeterminacionDetalleVM
            {
                Id = r.Id,
                ObraId = r.ObraId,
                ObraNombre = r.Obra.Nombre,
                ObraNumeroLicitacion = r.Obra.NumeroLicitacion,
                MesBase = r.MesBase,
                AnioBase = r.AnioBase,
                IdPublicacionBase = r.IdPublicacionBase,
                MesSalto = r.MesSalto,
                AnioSalto = r.AnioSalto,
                IdPublicacionSalto = r.IdPublicacionSalto,
                FechaGuardado = r.FechaGuardado,
                TotalKiK0 = r.TotalKiK0,
                PorcentajeAumento = r.PorcentajeAumento,
                Items = r.Items.OrderBy(i => i.Numero).Select(i => new RedeterminacionDetalleItemVM
                {
                    Id = i.Id,
                    Numero = i.Numero,
                    Insumo = i.Insumo,
                    PesoPorcentaje = i.PesoPorcentaje,
                    ValorMesBase = i.ValorMesBase,
                    ValorMesSalto = i.ValorMesSalto,
                    KiK0 = i.KiK0,
                    VariacionPonderada = i.VariacionPonderada,
                    DescripcionINDEC = i.DescripcionINDEC
                }).ToList()
            })
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Publicaciones cargadas con los períodos que cubre cada una, en orden CRONOLÓGICO
    /// (la más reciente última). El orden por publicación se hace en memoria con
    /// <see cref="PublicacionIndec.Orden"/>: ordenar el id en SQL es alfabético y, con
    /// el sufijo "MM_AA", pondría diciembre del año viejo después de enero del nuevo.
    /// </summary>
    public async Task<List<(string IdPublicacion, int Anio, int Mes)>> GetRevistasDisponiblesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var data = await db.ValoresIndice
            .Where(v => v.IdPublicacion != null)
            .Select(v => new { v.IdPublicacion, v.Anio, v.Mes })
            .Distinct()
            .ToListAsync();

        return data
            .OrderBy(x => PublicacionIndec.Orden(x.IdPublicacion))
            .ThenBy(x => x.Anio).ThenBy(x => x.Mes)
            .Select(x => (x.IdPublicacion!, x.Anio, x.Mes))
            .ToList();
    }
}
