using Microsoft.EntityFrameworkCore;
using SIGO.Data;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Models.ViewModels;
using SIGO.Services.Importacion;
using SIGO.Services.Validaciones;

namespace SIGO.Services;

/// <summary>
/// Estructuras de costos y sus ítems: todas las lecturas devuelven VMs sin tracking y
/// todas las mutaciones pasan por acá (las páginas no tocan el DbContext). Las reglas
/// de normalización viven puras en <see cref="EstructuraValidator"/>.
/// </summary>
public class EstructuraService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser)
{
    /// <summary>Defensa en profundidad (ver <see cref="Roles.Exigir"/>): módulo Certificaciones.</summary>
    private void ExigirRol(string accion) => Roles.Exigir(currentUser.IsInRole, Roles.Certificaciones, accion);

    // ── Estructuras ─────────────────────────────────────────────────────────────

    /// <summary>Obra para la cabecera del listado y del alta (null si no existe).</summary>
    public async Task<ObraResumenVM?> ObtenerObraAsync(int obraId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ResumenObraAsync(obraId);
    }

    /// <summary>Cards del listado: cada estructura con su total de ítems hoja y monto.</summary>
    public async Task<List<EstructuraVM>> ListarAsync(int obraId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.EstructurasCostos.AsNoTracking()
            .Where(e => e.ObraId == obraId)
            .OrderBy(e => e.FechaCreacion)
            .Select(e => new EstructuraVM
            {
                Id = e.Id,
                ObraId = e.ObraId,
                Nombre = e.Nombre,
                FechaCreacion = e.FechaCreacion,
                RowVersion = e.RowVersion,
                TotalItems = e.Items.Count(i => !i.EsAgrupador),
                MontoTotal = e.Items.Where(i => !i.EsAgrupador).Sum(i => i.Monto ?? 0)
            })
            .ToListAsync();
    }

    /// <summary>Crea la estructura y devuelve su Id (la página navega a cargar ítems).</summary>
    public async Task<int> CrearAsync(int obraId, EstructuraFormVM vm)
    {
        ExigirRol("crear la estructura");
        Validacion.Exigir(EstructuraValidator.NombreEstructura(vm.Nombre));

        await using var db = await dbFactory.CreateDbContextAsync();
        var estructura = new EstructuraCostos
        {
            ObraId = obraId,
            Nombre = vm.Nombre!,
            FechaCreacion = vm.FechaCreacion
        };
        db.EstructurasCostos.Add(estructura);
        await db.SaveChangesAsync();
        return estructura.Id;
    }

    /// <summary>
    /// Elimina la estructura (los ítems caen en cascada). Recarga fresca por Id con el
    /// RowVersion de la página como token: si otro usuario la modificó en el medio,
    /// salta el conflicto de concurrencia. Devuelve false si ya no existía —eliminada
    /// por otro usuario— y la página sigue su flujo normal (mismo desenlace que antes).
    /// </summary>
    public async Task<bool> EliminarAsync(int estructuraId, byte[] rowVersion)
    {
        ExigirRol("eliminar la estructura");
        await using var db = await dbFactory.CreateDbContextAsync();

        var estructura = await db.EstructurasCostos.FirstOrDefaultAsync(e => e.Id == estructuraId);
        if (estructura is null) return false; // ya la borró otro usuario

        db.Entry(estructura).Property(e => e.RowVersion).OriginalValue = rowVersion;
        db.EstructurasCostos.Remove(estructura);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Ítems ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Estructura completa para la página de ítems: cabecera de la obra + ítems
    /// ordenados por Orden (la UI los aplana con Arbol.Aplanar). Null si no existe.
    /// </summary>
    public async Task<EstructuraVM?> ObtenerConItemsAsync(int estructuraId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var estructura = await db.EstructurasCostos.AsNoTracking()
            .Include(e => e.Obra)
            .Include(e => e.Items.OrderBy(i => i.Orden))
            .FirstOrDefaultAsync(e => e.Id == estructuraId);
        if (estructura is null) return null;

        var items = estructura.Items.Select(MapItem).ToList();
        return new EstructuraVM
        {
            Id = estructura.Id,
            ObraId = estructura.ObraId,
            Nombre = estructura.Nombre,
            FechaCreacion = estructura.FechaCreacion,
            RowVersion = estructura.RowVersion,
            ObraNombre = estructura.Obra.Nombre,
            ObraNumeroLicitacion = estructura.Obra.NumeroLicitacion,
            Items = items,
            TotalItems = items.Count(i => !i.EsAgrupador),
            MontoTotal = items.Where(i => !i.EsAgrupador).Sum(i => i.Monto ?? 0)
        };
    }

    /// <summary>Ítem para el form de edición + agrupadores posibles como padre (null si no existe).</summary>
    public async Task<ItemParaEditarVM?> ObtenerItemAsync(int itemId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.ItemsEstructura.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId);
        if (item is null) return null;

        var agrupadores = await db.ItemsEstructura.AsNoTracking()
            .Where(i => i.EstructuraCostosId == item.EstructuraCostosId && i.EsAgrupador && i.Id != itemId)
            .OrderBy(i => i.Orden)
            .Select(i => new AgrupadorOpcionVM(i.Id, i.Descripcion))
            .ToListAsync();

        // Contexto para la cabecera de la página (obra + estructura). FirstOrDefault:
        // si la estructura se borró entre las dos queries (cascada en curso), el ítem
        // se trata como no encontrado en vez de tumbar la página al ErrorBoundary.
        var ctx = await db.EstructurasCostos.AsNoTracking()
            .Where(e => e.Id == item.EstructuraCostosId)
            .Select(e => new { e.ObraId, ObraNombre = e.Obra.Nombre, e.Nombre })
            .FirstOrDefaultAsync();
        if (ctx is null) return null;

        return new ItemParaEditarVM(MapItem(item), agrupadores, ctx.ObraId, ctx.ObraNombre, ctx.Nombre);
    }

    /// <summary>Agrega un ítem al final de la estructura (normalizado por el validator).</summary>
    public async Task AgregarItemAsync(int estructuraId, ItemEstructuraVM nuevo)
    {
        ExigirRol("agregar el ítem");
        Validacion.Exigir(EstructuraValidator.DescripcionItem(nuevo.Descripcion));
        // Alta: un agrupador nace en la raíz (conservarPadreDeAgrupador=false).
        var n = EstructuraValidator.Normalizar(nuevo, conservarPadreDeAgrupador: false);

        await using var db = await dbFactory.CreateDbContextAsync();
        db.ItemsEstructura.Add(NuevoItem(estructuraId, nuevo.EsAgrupador, n, await MaxOrdenAsync(db, estructuraId) + 1));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Actualiza los campos editables de un ítem (normalizados; la edición conserva el
    /// padre de los agrupadores — los sub-rubros anidados existen). El RowVersion de la
    /// carga viaja como token del UPDATE. Solo se copian los campos del form: el resto
    /// (TipoMovimiento, ItemOrigenId, Orden) queda como está en la DB.
    /// </summary>
    public async Task ActualizarItemAsync(ItemEstructuraVM vm)
    {
        ExigirRol("actualizar el ítem");
        Validacion.Exigir(EstructuraValidator.DescripcionItem(vm.Descripcion));
        var n = EstructuraValidator.Normalizar(vm, conservarPadreDeAgrupador: true);

        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.ItemsEstructura.FirstOrDefaultAsync(i => i.Id == vm.Id)
            // Borrado en el medio: mismo desenlace que el UPDATE con 0 filas afectadas
            // — conflicto de concurrencia (la página ofrece recargar), no "no encontrado".
            ?? throw new DbUpdateConcurrencyException(
                "El ítem fue eliminado por otro usuario mientras se editaba.");

        item.AgrupadorPadreId = n.AgrupadorPadreId;
        item.Codigo = n.Codigo;
        item.Descripcion = n.Descripcion;
        item.Unidad = n.Unidad;
        item.Cantidad = n.Cantidad;
        item.PUBasico = n.PUBasico;
        item.Monto = n.Monto;

        db.Entry(item).Property(i => i.RowVersion).OriginalValue = vm.RowVersion;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Elimina un ítem con el RowVersion de la página como token. Si es agrupador,
    /// primero desasocia a sus hijos (quedan en la raíz: eliminar el rubro no debe
    /// arrastrar los ítems). Devuelve false si ya no existía —eliminado por otro
    /// usuario— para que la página avise en vez de fallar.
    /// </summary>
    public async Task<bool> EliminarItemAsync(int itemId, byte[] rowVersion)
    {
        ExigirRol("eliminar el ítem");
        await using var db = await dbFactory.CreateDbContextAsync();

        var item = await db.ItemsEstructura.FirstOrDefaultAsync(i => i.Id == itemId);
        if (item is null) return false; // ya eliminado por otro usuario

        if (item.EsAgrupador)
        {
            var hijos = await db.ItemsEstructura.Where(i => i.AgrupadorPadreId == item.Id).ToListAsync();
            foreach (var hijo in hijos)
                hijo.AgrupadorPadreId = null;
        }

        db.Entry(item).Property(i => i.RowVersion).OriginalValue = rowVersion;
        db.ItemsEstructura.Remove(item);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Importación desde Excel ─────────────────────────────────────────────────

    /// <summary>Estrategia sobre los ítems que ya tiene la estructura al importar.</summary>
    public enum ModoImportacion
    {
        /// <summary>Los ítems importados se agregan al final; los existentes no se tocan.</summary>
        Agregar,
        /// <summary>Se eliminan todos los ítems existentes y se cargan los importados.</summary>
        Reemplazar
    }

    /// <summary>
    /// Persiste las filas analizadas por <see cref="ImportadorEstructura"/> en una sola
    /// transacción: rubros y sub-rubros como agrupadores (sin código, como el resto de
    /// la app), ítems normalizados con el validator y el orden a continuación del
    /// último. El RowVersion de la página viaja como token del UPDATE de la estructura
    /// (detecta renombres o borrados concurrentes). Como ese token no cambia cuando otro
    /// usuario agrega o quita ítems, Reemplazar exige además que el conjunto de ítems
    /// actual sea el que la página vio (<paramref name="itemsVistos"/>; Agregar no lo
    /// usa): si difiere, conflicto de concurrencia. Reemplazar solo se admite si ningún
    /// certificado tiene un bloque sobre la estructura (aunque todavía no haya guardado
    /// porcentajes) y si ningún ítem de otra estructura (BED) toma uno de estos como origen.
    /// Devuelve la cantidad de filas creadas (agrupadores incluidos).
    /// </summary>
    public async Task<int> ImportarItemsAsync(int estructuraId, IReadOnlyList<FilaEstructuraImport> filas,
        ModoImportacion modo, byte[] rowVersion, IReadOnlyCollection<int> itemsVistos)
    {
        ExigirRol("importar los ítems");

        var aImportar = filas.Where(f => f.SeImporta).ToList();
        if (aImportar.Count == 0)
            throw new InvalidOperationException("No hay filas para importar.");
        foreach (var f in aImportar)
            Validacion.Exigir(EstructuraValidator.DescripcionItem(f.Descripcion));

        await using var db = await dbFactory.CreateDbContextAsync();
        var estructura = await db.EstructurasCostos.FirstOrDefaultAsync(e => e.Id == estructuraId)
            ?? throw new InvalidOperationException("La estructura ya no existe.");

        db.AplicarTokenSesion(estructura, rowVersion, e => e.Nombre);

        // Serializable: los chequeos de Reemplazar (ítems vistos, bloques de certificado,
        // orígenes de un BED) y el Max+1 de Agregar miran filas que el RowVersion de la
        // estructura no protege; sin esto, un alta concurrente entre el chequeo y el
        // borrado se perdería en silencio.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var itemsDeEstructura = db.ItemsEstructura.Where(i => i.EstructuraCostosId == estructuraId);

        var orden = 0;
        if (modo == ModoImportacion.Reemplazar)
        {
            // Bloques, no filas de ítems: un certificado en borrador con el bloque recién
            // agregado y sin % guardados todavía no tiene ItemsCertificado.
            var enCertificados = await db.CertificadoEstructuras
                .AnyAsync(ce => ce.EstructuraCostosId == estructuraId);
            if (enCertificados)
                throw new InvalidOperationException(
                    "No se puede reemplazar: hay certificados con un bloque sobre esta estructura. Importá en modo Agregar o quitá primero esos bloques.");

            var actuales = await itemsDeEstructura.Select(i => i.Id).ToListAsync();
            if (!actuales.ToHashSet().SetEquals(itemsVistos))
                throw new DbUpdateConcurrencyException(
                    "Otro usuario agregó o eliminó ítems de la estructura mientras se analizaba la planilla.");

            var comoOrigen = await db.ItemsEstructura
                .AnyAsync(i => i.ItemOrigen != null && i.ItemOrigen.EstructuraCostosId == estructuraId
                               && i.EstructuraCostosId != estructuraId);
            if (comoOrigen)
                throw new InvalidOperationException(
                    "No se puede reemplazar: ítems de otra estructura (BED) referencian a los de esta.");

            // Primero se desarman las dos FK self-referenciales (padre → hijo e ítem origen,
            // ambas Restrict) y después se borra.
            await itemsDeEstructura.ExecuteUpdateAsync(s => s
                .SetProperty(i => i.AgrupadorPadreId, (int?)null)
                .SetProperty(i => i.ItemOrigenId, (int?)null));
            await itemsDeEstructura.ExecuteDeleteAsync();
        }
        else
        {
            orden = await MaxOrdenAsync(db, estructuraId);
        }

        // El padre se resuelve por índice dentro de la lista: la navegación deja que EF
        // asigne las FK self-referenciales en el mismo SaveChanges.
        var entidades = new Dictionary<int, ItemEstructura>();
        for (var i = 0; i < filas.Count; i++)
        {
            var f = filas[i];
            if (!f.SeImporta) continue;

            var n = EstructuraValidator.Normalizar(f.EsAgrupador, null, f.Codigo, f.Descripcion, f.Unidad, f.Cantidad, f.PUBasico);
            var item = NuevoItem(estructuraId, f.EsAgrupador, n, ++orden);
            item.TipoMovimiento = f.TipoMovimiento ?? TipoMovimiento.Normal;
            if (f.PadreIndice is int p && entidades.TryGetValue(p, out var padre))
                item.AgrupadorPadre = padre;

            entidades[i] = item;
            db.ItemsEstructura.Add(item);
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return entidades.Count;
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Último Orden de la estructura (0 si no tiene ítems), leído de la DB y no de la
    /// colección en memoria de la página: otro usuario pudo haber agregado ítems desde
    /// que se cargó.
    /// </summary>
    private static async Task<int> MaxOrdenAsync(AppDbContext db, int estructuraId) =>
        await db.ItemsEstructura
            .Where(i => i.EstructuraCostosId == estructuraId)
            .MaxAsync(i => (int?)i.Orden) ?? 0;

    /// <summary>Ítem nuevo con los campos ya normalizados por el validator (el alta manual y la importación lo arman igual).</summary>
    private static ItemEstructura NuevoItem(int estructuraId, bool esAgrupador, EstructuraValidator.ItemNormalizado n, int orden) => new()
    {
        EstructuraCostosId = estructuraId,
        EsAgrupador = esAgrupador,
        AgrupadorPadreId = n.AgrupadorPadreId,
        Codigo = n.Codigo,
        Descripcion = n.Descripcion,
        Unidad = n.Unidad,
        Cantidad = n.Cantidad,
        PUBasico = n.PUBasico,
        Monto = n.Monto,
        Orden = orden
    };

    private static ItemEstructuraVM MapItem(ItemEstructura i) => new()
    {
        Id = i.Id,
        EstructuraCostosId = i.EstructuraCostosId,
        EsAgrupador = i.EsAgrupador,
        AgrupadorPadreId = i.AgrupadorPadreId,
        Orden = i.Orden,
        Codigo = i.Codigo,
        Descripcion = i.Descripcion,
        Unidad = i.Unidad,
        Cantidad = i.Cantidad,
        PUBasico = i.PUBasico,
        Monto = i.Monto,
        RowVersion = i.RowVersion
    };
}
