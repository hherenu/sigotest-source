using Microsoft.EntityFrameworkCore;
using SIGO.Data;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Models.ViewModels;
using SIGO.Services.Validaciones;

namespace SIGO.Services;

public class CertificadoService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser)
{
    /// <summary>Defensa en profundidad (ver <see cref="Roles.Exigir"/>): módulo Certificaciones.</summary>
    private void ExigirRol(string accion) => Roles.Exigir(currentUser.IsInRole, Roles.Certificaciones, accion);

    /// <summary>
    /// Movimiento del mes de un ítem a partir del % cargado. Base de cálculo en
    /// cascada: cantidad×PU solo si el contrato define AMBOS; si no (ítems globales
    /// "1 gl", o con PU pero sin cantidad), el monto sale directo del % sobre el
    /// monto de contrato. Redondeos intermedios a 4 decimales.
    /// ÚNICA implementación de la fórmula: la usan GuardarItemsAsync (lo que se
    /// guarda) y el LiveMonto de CertificadoCargar (lo que se muestra en vivo) —
    /// antes eran dos copias que podían derivar en diferencias de centavos.
    /// </summary>
    public static (decimal Cantidad, decimal Monto) MovimientoDeMes(
        decimal? cantidadContrato, decimal? puBasico, decimal? montoContrato, decimal pct)
    {
        var cantidad = Math.Round((cantidadContrato ?? 0) * pct / 100m, 4);
        var monto = cantidadContrato.HasValue && puBasico.HasValue
            ? Math.Round(cantidad * puBasico.Value, 4)
            : montoContrato.HasValue
                ? Math.Round(montoContrato.Value * pct / 100m, 4)
                : 0m;
        return (cantidad, monto);
    }

    /// <summary>
    /// Porcentaje de avance de un ítem según la base que el contrato defina: sobre la
    /// cantidad si la tiene; si no (ítems globales) sobre el monto de contrato; null si
    /// no hay ninguna base.
    /// </summary>
    private static decimal? PctDe(decimal cantidad, decimal monto, decimal? cantContrato, decimal? montoContrato) =>
        cantContrato is decimal c && c != 0 ? Math.Round(cantidad * 100m / c, 4)
        : montoContrato is decimal m && m != 0 ? Math.Round(monto * 100m / m, 4)
        : null;

    /// <summary>
    /// Acumulado previo de un ítem y sus % por base contractual. Las sumas sobre los
    /// certificados cerrados anteriores y las dos llamadas a PctDe son la ÚNICA
    /// implementación de la regla: la usan BuildVmAsync (borrador) y CerrarAsync
    /// (snapshot) — eran dos copias con matices que ya habían derivado una vez.
    /// Los % "PorBase" vienen null cuando el ítem no tiene base de cantidad ni monto:
    /// cada punto de uso decide su fallback sobre <see cref="AcumuladosItem.PctAnteriorDirecto"/>
    /// (Cerrar guarda el número; el VM muestra vacío cuando es 0).
    /// </summary>
    public static AcumuladosItem AcumuladosDeItem(
        IEnumerable<ItemCertificado> anteriores, ItemEstructura item, decimal cantMes, decimal montoMes)
    {
        decimal cantAnt = 0, montoAnt = 0, pctAntDirecto = 0;
        foreach (var a in anteriores)
        {
            if (a.ItemEstructuraId != item.Id) continue;
            cantAnt += a.CantidadActual;
            montoAnt += a.MontoActual;
            pctAntDirecto += a.PorcentajeActual;
        }

        return new AcumuladosItem(
            cantAnt, montoAnt, pctAntDirecto,
            PctAnteriorPorBase: PctDe(cantAnt, montoAnt, item.Cantidad, item.Monto),
            PctAcumuladoPorBase: PctDe(cantAnt + cantMes, montoAnt + montoMes, item.Cantidad, item.Monto));
    }

    /// <summary>Resultado de <see cref="AcumuladosDeItem"/>.</summary>
    public sealed record AcumuladosItem(
        decimal CantidadAnterior, decimal MontoAnterior, decimal PctAnteriorDirecto,
        decimal? PctAnteriorPorBase, decimal? PctAcumuladoPorBase);

    /// <summary>
    /// Construye el VM de un certificado con todos sus bloques (null si no existe).
    /// En borrador calcula los acumulados al vuelo desde los certificados anteriores
    /// cerrados; si el certificado está cerrado, lee el snapshot congelado.
    /// </summary>
    public async Task<CertificadoVM?> BuildVmAsync(int certificadoId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // BuildVm es solo lectura: todas sus queries van sin tracking.
        var certificado = await db.Certificados.AsNoTracking()
            .Include(c => c.Obra)
            .FirstOrDefaultAsync(c => c.Id == certificadoId);
        if (certificado is null) return null;

        var bloques = await db.CertificadoEstructuras.AsNoTracking()
            .Where(b => b.CertificadoId == certificado.Id)
            .OrderBy(b => b.Orden)
            .ToListAsync();

        var vm = new CertificadoVM
        {
            CertificadoId = certificado.Id,
            Numero = certificado.Numero,
            Mes = certificado.Mes,
            Anio = certificado.Anio,
            Estado = certificado.Estado,
            FechaEmision = certificado.FechaEmision,
            Observaciones = certificado.Observaciones,
            ObraId = certificado.ObraId,
            ObraNombre = certificado.Obra.Nombre,
            RowVersion = certificado.RowVersion
        };
        var cerrado = CertificadoValidator.EsCerrado(certificado.Estado);

        // Todo lo que el foreach necesita se trae en 3 queries batcheadas (antes eran
        // 3-4 POR bloque): estructuras con ítems, filas cargadas y acumulados previos.
        var idsEstructuras = bloques.Select(b => b.EstructuraCostosId).Distinct().ToList();
        var estructuras = await db.EstructurasCostos.AsNoTracking()
            .Include(e => e.Items.OrderBy(i => i.Orden))
            .Where(e => idsEstructuras.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        var idsBloques = bloques.Select(b => b.Id).ToList();
        var actualesPorBloque = (await db.ItemsCertificado.AsNoTracking()
                .Where(ic => idsBloques.Contains(ic.CertificadoEstructuraId))
                .ToListAsync())
            .ToLookup(ic => ic.CertificadoEstructuraId);

        // Anteriores: solo en borrador (cerrado usa snapshot)
        var anterioresPorEstructura = cerrado
            ? Array.Empty<ItemCertificado>().ToLookup(ic => 0)
            : await CargarAnterioresAsync(db, idsEstructuras, certificado);

        foreach (var bloque in bloques)
        {
            // GetValueOrDefault + throw manejable: una fila huérfana (solo posible por
            // fuera de la app, la FK es Restrict) debe notificarse, no tumbar el circuito.
            var estructura = estructuras.GetValueOrDefault(bloque.EstructuraCostosId)
                ?? throw new InvalidOperationException("La estructura de costos del bloque ya no existe.");
            var actuales = actualesPorBloque[bloque.Id].ToList();
            var anteriores = anterioresPorEstructura[bloque.EstructuraCostosId];

            // En un certificado cerrado, un ítem sin fila de snapshot fue agregado a la
            // estructura DESPUÉS del cierre: no forma parte del certificado emitido y no
            // se muestra (el cierre congela una fila para todos los ítems del momento).
            // Los agrupadores no tienen fila propia y siempre se renderizan.
            var vmItems = estructura.Items
                .Where(item => !cerrado || item.EsAgrupador
                            || actuales.Any(a => a.ItemEstructuraId == item.Id))
                .Select(item =>
            {
                var actual = actuales.FirstOrDefault(a => a.ItemEstructuraId == item.Id);

                decimal cantAnt, montoAnt, cantMes, montoMes;
                decimal? cantContrato, puBasico, montoContrato;
                decimal? pctAnt, pctMes, pctAcum;

                if (cerrado && actual is not null)
                {
                    // Snapshot congelado: cantidades, contrato y porcentajes tal como
                    // quedaron al cerrar (editar la estructura después no los altera).
                    // El fallback ?? cubre certificados cerrados antes de existir el snapshot de contrato.
                    cantAnt  = actual.CantidadAnterior ?? 0;
                    montoAnt = actual.MontoAnterior ?? 0;
                    cantMes  = actual.CantidadActual;
                    montoMes = actual.MontoActual;
                    cantContrato  = actual.CantidadContrato ?? item.Cantidad;
                    puBasico      = actual.PUBasico ?? item.PUBasico;
                    montoContrato = actual.MontoContrato ?? item.Monto;
                    pctAnt  = actual.PorcentajeAnterior;
                    pctMes  = actual.PorcentajeActual;
                    pctAcum = actual.PorcentajeAcumulado;
                }
                else
                {
                    cantMes  = actual?.CantidadActual ?? 0;
                    montoMes = actual?.MontoActual ?? 0;
                    cantContrato  = item.Cantidad;
                    puBasico      = item.PUBasico;
                    montoContrato = item.Monto;
                    // % Mes: el valor guardado tal como se tipeó (no recalculado desde la
                    // cantidad redondeada, que introducía deriva al reabrir el editor).
                    pctMes  = actual?.PorcentajeActual;

                    var acum = AcumuladosDeItem(anteriores, item, cantMes, montoMes);
                    cantAnt  = acum.CantidadAnterior;
                    montoAnt = acum.MontoAnterior;
                    // Sin base contractual, el avance se acumula directamente en % (única
                    // base disponible); en el VM un acumulado 0 se muestra vacío.
                    pctAnt  = acum.PctAnteriorPorBase
                              ?? (acum.PctAnteriorDirecto != 0 ? acum.PctAnteriorDirecto : null);
                    pctAcum = acum.PctAcumuladoPorBase
                              ?? (acum.PctAnteriorDirecto + (pctMes ?? 0) is var t && t != 0 ? t : null);
                }

                return new ItemCertificadoVM
                {
                    ItemEstructuraId = item.Id,
                    Codigo = item.Codigo,
                    Descripcion = item.Descripcion,
                    Unidad = item.Unidad,
                    EsAgrupador = item.EsAgrupador,
                    AgrupadorPadreId = item.AgrupadorPadreId,
                    Orden = item.Orden,
                    CantidadContrato = cantContrato,
                    PUBasico = puBasico,
                    MontoContrato = montoContrato,
                    CantidadAnterior = cantAnt,
                    MontoAnterior = montoAnt,
                    CantidadMes = cantMes,
                    MontoMes = montoMes,
                    PorcentajeAnterior = pctAnt,
                    PorcentajeMes = pctMes,
                    PorcentajeAcumulado = pctAcum
                };
            })
            .ToList();

            PropagateAgrupadores(vmItems);

            var hojas = vmItems.Where(i => !i.EsAgrupador).ToList();
            vm.Bloques.Add(new BloqueCertificadoVM
            {
                CertificadoEstructuraId = bloque.Id,
                EstructuraCostosId = bloque.EstructuraCostosId,
                Titulo = bloque.Titulo ?? estructura.Nombre,
                Orden = bloque.Orden,
                Items = vmItems,
                TotalMontoContrato = hojas.Sum(i => i.MontoContrato ?? 0),
                TotalMontoAnterior = hojas.Sum(i => i.MontoAnterior),
                TotalMontoMes = hojas.Sum(i => i.MontoMes)
            });
        }

        return vm;
    }

    /// <summary>
    /// Contexto de la página de alta de certificado: datos de la obra, estructuras
    /// elegibles y el form pre-cargado (próximo número, mes/año actuales).
    /// Null si la obra no existe.
    /// </summary>
    public async Task<CertificadoAltaVM?> BuildAltaVmAsync(int obraId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var obra = await db.ResumenObraAsync(obraId);
        if (obra is null) return null;

        var estructuras = await db.EstructurasCostos.AsNoTracking()
            .Where(e => e.ObraId == obraId)
            .OrderBy(e => e.FechaCreacion)
            .Select(e => new EstructuraOpcionVM { Id = e.Id, Nombre = e.Nombre })
            .ToListAsync();

        var ultimoNum = await db.Certificados
            .Where(c => c.ObraId == obraId)
            .MaxAsync(c => (int?)c.Numero) ?? 0;

        return new CertificadoAltaVM
        {
            ObraId = obraId,
            ObraNombre = obra.Nombre,
            NumeroLicitacion = obra.NumeroLicitacion,
            Estructuras = estructuras,
            Form = new CertificadoNuevoVM
            {
                Numero = ultimoNum + 1,
                Mes = DateTime.Today.Month,
                Anio = DateTime.Today.Year,
                FechaEmision = DateTime.Today,
                EstructuraCostosId = estructuras.FirstOrDefault()?.Id ?? 0
            }
        };
    }

    /// <summary>Obra para la cabecera del listado de certificados (null si no existe).</summary>
    public async Task<ObraResumenVM?> ObtenerObraAsync(int obraId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ResumenObraAsync(obraId);
    }

    /// <summary>
    /// Filas del listado de certificados de la obra, ordenadas por número. Solo
    /// lectura: proyección AsNoTracking a VM, sin entidades EF. Los títulos de los
    /// bloques viajan como lista y se unen en memoria (string.Join no traduce a SQL).
    /// </summary>
    public async Task<List<CertificadoListItemVM>> ListarAsync(int obraId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var filas = await db.Certificados.AsNoTracking()
            .Where(c => c.ObraId == obraId)
            .OrderBy(c => c.Numero)
            .Select(c => new
            {
                c.Id,
                c.Numero,
                c.Mes,
                c.Anio,
                c.Estado,
                c.FechaEmision,
                c.Observaciones,
                Titulos = c.Estructuras
                    .OrderBy(b => b.Orden)
                    .Select(b => b.Titulo ?? b.EstructuraCostos.Nombre)
                    .ToList()
            })
            .ToListAsync();

        return filas.Select(f => new CertificadoListItemVM
        {
            Id = f.Id,
            Numero = f.Numero,
            Mes = f.Mes,
            Anio = f.Anio,
            Estado = f.Estado,
            FechaEmision = f.FechaEmision,
            Observaciones = f.Observaciones,
            Bloques = string.Join(", ", f.Titulos)
        }).ToList();
    }

    /// <summary>
    /// Crea un certificado en borrador con su primer bloque. Devuelve el Id creado, o
    /// el rechazo de negocio (patrón `rechazo`: la página lo notifica como "No se pudo
    /// crear" sin pasar por excepciones). Las reglas de numeración viven en
    /// <see cref="CertificadoValidator.NumeroNuevo"/> (fuente única, testeable).
    /// La entidad se arma NUEVA en cada llamada: un retry tras un fallo no puede
    /// arrastrar el bloque agregado en el intento anterior (violaba el índice único).
    /// </summary>
    public async Task<(int? CertificadoId, string? Rechazo)> CrearAsync(int obraId, CertificadoNuevoVM vm)
    {
        ExigirRol("crear el certificado");
        await using var db = await dbFactory.CreateDbContextAsync();

        var yaExiste = await db.Certificados.AnyAsync(c => c.ObraId == obraId && c.Numero == vm.Numero);
        var maxNumero = await db.Certificados
            .Where(c => c.ObraId == obraId)
            .MaxAsync(c => (int?)c.Numero) ?? 0;
        var ultimoCerrado = await db.Certificados
            .Where(c => c.ObraId == obraId &&
                        (c.Estado == EstadoCertificado.Cerrado || c.Estado == EstadoCertificado.Aprobado))
            .MaxAsync(c => (int?)c.Numero) ?? 0;

        var rechazo = CertificadoValidator.NumeroNuevo(vm.Numero, yaExiste, maxNumero, ultimoCerrado);
        if (rechazo is not null) return (null, rechazo);

        var estructuraValida = vm.EstructuraCostosId > 0 &&
            await db.EstructurasCostos.AnyAsync(e => e.Id == vm.EstructuraCostosId && e.ObraId == obraId);
        if (!estructuraValida)
            return (null, "Debe seleccionar una estructura de costos válida.");

        var nuevo = new Certificado
        {
            ObraId = obraId,
            Numero = vm.Numero,
            Mes = vm.Mes,
            Anio = vm.Anio,
            FechaEmision = vm.FechaEmision,
            Observaciones = vm.Observaciones,
            Estructuras = { new CertificadoEstructura { EstructuraCostosId = vm.EstructuraCostosId, Orden = 1 } }
        };
        db.Certificados.Add(nuevo);
        await db.SaveChangesAsync();
        return (nuevo.Id, null);
    }

    /// <summary>Motivo del rechazo al agregar/quitar un bloque (la página elige título y severidad).</summary>
    public enum MotivoRechazoBloque { NoEditable, Duplicado, EstructuraInvalida }

    /// <summary>
    /// Resultado de agregar/quitar bloque: Encontrado=false si el certificado/bloque ya
    /// no existe (patrón bool-false — la página recarga sin notificar error); Rechazo con
    /// el mensaje de la regla de negocio que lo frenó (null = operación completada).
    /// </summary>
    public sealed record ResultadoBloque(bool Encontrado, MotivoRechazoBloque? Motivo = null, string? Rechazo = null);

    /// <summary>
    /// Agrega a un certificado en borrador un bloque de la estructura indicada
    /// (validaciones puras en <see cref="CertificadoValidator.AgregarBloque"/>).
    /// </summary>
    public async Task<ResultadoBloque> AgregarBloqueAsync(int certificadoId, int estructuraCostosId)
    {
        ExigirRol("agregar el bloque");
        await using var db = await dbFactory.CreateDbContextAsync();

        var cert = await db.Certificados.Include(c => c.Estructuras)
            .FirstOrDefaultAsync(c => c.Id == certificadoId);
        if (cert is null) return new ResultadoBloque(false);

        var duplicado = cert.Estructuras.Any(b => b.EstructuraCostosId == estructuraCostosId);
        var deLaObra = await db.EstructurasCostos
            .AnyAsync(e => e.Id == estructuraCostosId && e.ObraId == cert.ObraId);

        var rechazo = CertificadoValidator.AgregarBloque(cert.Estado, duplicado, deLaObra);
        if (rechazo is not null)
            return new ResultadoBloque(true,
                cert.Estado != EstadoCertificado.Borrador ? MotivoRechazoBloque.NoEditable
                : duplicado ? MotivoRechazoBloque.Duplicado
                : MotivoRechazoBloque.EstructuraInvalida,
                rechazo);

        // RowVersion del certificado: si otro usuario lo cierra entre el chequeo y
        // el save, el UPDATE falla por concurrencia en vez de agregar el bloque.
        db.Entry(cert).Property(c => c.Estado).IsModified = true;
        var orden = cert.Estructuras.Count > 0 ? cert.Estructuras.Max(b => b.Orden) + 1 : 1;
        db.CertificadoEstructuras.Add(new CertificadoEstructura
        {
            CertificadoId = certificadoId,
            EstructuraCostosId = estructuraCostosId,
            Orden = orden
        });
        await db.SaveChangesAsync();
        return new ResultadoBloque(true);
    }

    /// <summary>Quita un bloque de un certificado en borrador (con sus ítems cargados).</summary>
    public async Task<ResultadoBloque> QuitarBloqueAsync(int certificadoEstructuraId)
    {
        ExigirRol("quitar el bloque");
        await using var db = await dbFactory.CreateDbContextAsync();

        var b = await db.CertificadoEstructuras.Include(x => x.Certificado)
            .FirstOrDefaultAsync(x => x.Id == certificadoEstructuraId);
        if (b is null) return new ResultadoBloque(false);

        var rechazo = CertificadoValidator.QuitarBloque(b.Certificado.Estado);
        if (rechazo is not null)
            return new ResultadoBloque(true, MotivoRechazoBloque.NoEditable, rechazo);

        // Mismo patrón que al agregar: el RowVersion del certificado detecta un cierre
        // concurrente entre el chequeo y el save.
        db.Entry(b.Certificado).Property(c => c.Estado).IsModified = true;
        db.CertificadoEstructuras.Remove(b);
        await db.SaveChangesAsync();
        return new ResultadoBloque(true);
    }

    /// <summary>
    /// Estructuras de la obra que todavía no están usadas como bloque (para el dropdown
    /// "Agregar bloque" de la carga).
    /// </summary>
    public async Task<List<EstructuraOpcionVM>> EstructurasDisponiblesAsync(int obraId, IEnumerable<int> estructurasUsadas)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var usadas = estructurasUsadas.ToList();
        return await db.EstructurasCostos.AsNoTracking()
            .Where(e => e.ObraId == obraId && !usadas.Contains(e.Id))
            .OrderBy(e => e.FechaCreacion)
            .Select(e => new EstructuraOpcionVM { Id = e.Id, Nombre = e.Nombre })
            .ToListAsync();
    }

    /// <summary>
    /// Guarda los ítems de cada bloque de un certificado en borrador. Las claves
    /// externas son IDs de bloque; las internas IDs de ItemEstructura; el valor es
    /// el porcentaje del mes (puede ser negativo).
    /// </summary>
    public async Task GuardarItemsAsync(int certificadoId, Dictionary<int, Dictionary<int, decimal>> porcentajesPorBloque,
        byte[]? rowVersionSesion = null)
    {
        ExigirRol("guardar el certificado");
        await using var db = await dbFactory.CreateDbContextAsync();

        var cert = await db.Certificados
            .Include(c => c.Estructuras).ThenInclude(b => b.Items)
            .FirstOrThrowAsync(c => c.Id == certificadoId, "Certificado no encontrado.");

        Validacion.Exigir(CertificadoValidator.Guardar(cert.Estado));

        // Token de la sesión + touch del Estado: lost updates entre sesiones y carrera
        // guardar-vs-cerrar terminan en conflicto de concurrencia (ver la extensión).
        db.AplicarTokenSesion(cert, rowVersionSesion, c => c.Estado);

        foreach (var bloque in cert.Estructuras)
        {
            if (!porcentajesPorBloque.TryGetValue(bloque.Id, out var porcentajes)) continue;

            var estructura = await db.EstructurasCostos
                .Include(e => e.Items)
                .FirstAsync(e => e.Id == bloque.EstructuraCostosId);

            // El guardado recrea las filas: lo que no viene del editor (observaciones,
            // override de TipoMovimiento) se arrastra de la fila anterior del mismo ítem.
            var previos = bloque.Items.ToDictionary(i => i.ItemEstructuraId);
            db.ItemsCertificado.RemoveRange(bloque.Items);

            foreach (var (itemEstructuraId, pct) in porcentajes)
            {
                if (pct == 0) continue;

                var itemEst = estructura.Items.FirstOrDefault(i => i.Id == itemEstructuraId);
                if (itemEst is null || itemEst.EsAgrupador) continue;

                var (cantidad, monto) = MovimientoDeMes(itemEst.Cantidad, itemEst.PUBasico, itemEst.Monto, pct);

                var previo = previos.GetValueOrDefault(itemEstructuraId);
                db.ItemsCertificado.Add(new ItemCertificado
                {
                    CertificadoEstructuraId = bloque.Id,
                    ItemEstructuraId = itemEstructuraId,
                    TipoMovimiento = previo?.TipoMovimiento ?? itemEst.TipoMovimiento,
                    Observaciones = previo?.Observaciones,
                    CantidadActual = cantidad,
                    MontoActual = monto,
                    PorcentajeActual = Math.Round(pct, 4)
                });
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Cierra el certificado: congela el snapshot (Anterior/Actual/Acumulado de cada
    /// ítem y subtotales de cada bloque) y marca Estado=Cerrado + FechaCierre.
    /// </summary>
    public async Task CerrarAsync(int certificadoId)
    {
        ExigirRol("cerrar el certificado");
        await using var db = await dbFactory.CreateDbContextAsync();
        // Serializable: el chequeo de orden mira OTRAS filas de la obra (el RowVersion
        // propio no las cubre) — sin esto, cerrar el N°3 y reabrir el N°2 en paralelo
        // pasan ambos chequeos y violan el invariante de cierre en orden.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var cert = await db.Certificados
            .Include(c => c.Estructuras).ThenInclude(b => b.Items)
            .FirstOrThrowAsync(c => c.Id == certificadoId, "Certificado no encontrado.");

        var previos = await db.Certificados
            .Where(c => c.ObraId == cert.ObraId && c.Numero < cert.Numero)
            .Select(c => new { c.Numero, c.Estado })
            .ToListAsync();

        Validacion.Exigir(CertificadoValidator.Cerrar(
            cert.Estado, cert.Numero, previos.Select(p => (p.Numero, p.Estado))));

        // Estructuras (solo lectura acá) y acumulados previos en 2 queries batcheadas,
        // en vez de 3 por bloque.
        var idsEstructuras = cert.Estructuras.Select(b => b.EstructuraCostosId).Distinct().ToList();
        var estructuras = await db.EstructurasCostos.AsNoTracking()
            .Include(e => e.Items)
            .Where(e => idsEstructuras.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);
        var anterioresPorEstructura = await CargarAnterioresAsync(db, idsEstructuras, cert);

        foreach (var bloque in cert.Estructuras)
        {
            // Mismo criterio que BuildVmAsync: huérfana → excepción manejable.
            var estructura = estructuras.GetValueOrDefault(bloque.EstructuraCostosId)
                ?? throw new InvalidOperationException("La estructura de costos del bloque ya no existe.");
            var anteriores = anterioresPorEstructura[bloque.EstructuraCostosId];

            decimal subAnterior = 0, subActual = 0;

            foreach (var item in estructura.Items.Where(i => !i.EsAgrupador))
            {
                var row = bloque.Items.FirstOrDefault(r => r.ItemEstructuraId == item.Id);

                var cantAct  = row?.CantidadActual ?? 0;
                var montoAct = row?.MontoActual ?? 0;
                var acum = AcumuladosDeItem(anteriores, item, cantAct, montoAct);
                var cantAnt  = acum.CantidadAnterior;
                var montoAnt = acum.MontoAnterior;

                // También los ítems sin movimiento ni acumulado reciben fila: sin ella,
                // el detalle/Excel del cerrado les mostraba el contrato VIVO de la
                // estructura (editar un ítem nunca certificado cambiaba retroactivamente
                // el total de contrato y el % de avance del certificado emitido).
                if (row is null)
                {
                    row = new ItemCertificado
                    {
                        CertificadoEstructuraId = bloque.Id,
                        ItemEstructuraId = item.Id,
                        TipoMovimiento = item.TipoMovimiento
                    };
                    db.ItemsCertificado.Add(row);
                }

                row.CantidadActual = cantAct;
                row.MontoActual = montoAct;
                // PorcentajeActual NO se recalcula: la carga ya lo guardó exacto como se
                // tipeó; derivarlo de la cantidad redondeada lo corría, y para ítems sin
                // cantidad de contrato lo pisaba con 0 (perdiendo la carga del usuario).
                row.CantidadAnterior = cantAnt;
                row.MontoAnterior = montoAnt;
                // Sin base de cantidad/monto, el snapshot guarda la suma directa de %.
                row.PorcentajeAnterior = acum.PctAnteriorPorBase ?? acum.PctAnteriorDirecto;
                row.CantidadAcumulada = cantAnt + cantAct;
                row.MontoAcumulado = montoAnt + montoAct;
                row.PorcentajeAcumulado = acum.PctAcumuladoPorBase
                                          ?? acum.PctAnteriorDirecto + row.PorcentajeActual;

                // Contrato congelado: el detalle y el Excel de un certificado cerrado no
                // deben cambiar si después se edita la estructura de costos.
                row.CantidadContrato = item.Cantidad;
                row.PUBasico = item.PUBasico;
                row.MontoContrato = item.Monto;

                subAnterior += montoAnt;
                subActual += montoAct;
            }

            bloque.SubtotalAnterior = subAnterior;
            bloque.SubtotalActual = subActual;
            bloque.SubtotalAcumulado = subAnterior + subActual;
        }

        cert.Estado = EstadoCertificado.Cerrado;
        cert.FechaCierre = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>
    /// Reabre un certificado cerrado a borrador: limpia el snapshot congelado y
    /// elimina las filas auto-generadas sin movimiento.
    /// </summary>
    public async Task ReabrirAsync(int certificadoId)
    {
        ExigirRol("reabrir el certificado");
        await using var db = await dbFactory.CreateDbContextAsync();
        // Serializable por el mismo motivo que CerrarAsync: el chequeo de posteriores
        // cerrados mira otras filas de la obra.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var cert = await db.Certificados
            .Include(c => c.Estructuras).ThenInclude(b => b.Items)
            .FirstOrThrowAsync(c => c.Id == certificadoId, "Certificado no encontrado.");

        var posterioresCerrados = await db.Certificados
            .Where(c => c.ObraId == cert.ObraId
                     && c.Numero > cert.Numero
                     && (c.Estado == EstadoCertificado.Cerrado || c.Estado == EstadoCertificado.Aprobado))
            .Select(c => c.Numero)
            .ToListAsync();

        Validacion.Exigir(CertificadoValidator.Reabrir(cert.Estado, cert.Numero, posterioresCerrados));

        foreach (var bloque in cert.Estructuras)
        {
            // Solo las filas auto-generadas por el cierre (sin ningún movimiento): una
            // fila con porcentaje cargado pero cantidad 0 (ítem sin cantidad de contrato)
            // es carga del usuario y debe sobrevivir a la reapertura.
            var zeroRows = bloque.Items
                .Where(i => i.CantidadActual == 0 && i.MontoActual == 0 && i.PorcentajeActual == 0)
                .ToList();
            db.ItemsCertificado.RemoveRange(zeroRows);

            foreach (var i in bloque.Items.Where(x => !zeroRows.Contains(x)))
            {
                i.CantidadAnterior = null;
                i.MontoAnterior = null;
                i.PorcentajeAnterior = null;
                i.CantidadAcumulada = null;
                i.MontoAcumulado = null;
                i.PorcentajeAcumulado = null;
                i.CantidadContrato = null;
                i.PUBasico = null;
                i.MontoContrato = null;
            }

            bloque.SubtotalAnterior = null;
            bloque.SubtotalActual = null;
            bloque.SubtotalAcumulado = null;
        }

        cert.Estado = EstadoCertificado.Borrador;
        cert.FechaCierre = null;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>Aprueba un certificado cerrado (no toca el snapshot, solo el estado).</summary>
    public Task AprobarAsync(int certificadoId) =>
        TransicionSimpleAsync(certificadoId, "aprobar el certificado",
            CertificadoValidator.Aprobar, EstadoCertificado.Aprobado);

    /// <summary>
    /// Vuelve un certificado aprobado a Cerrado (el snapshot queda intacto): es el
    /// camino de salida de una aprobación accidental, que antes solo se resolvía por DB.
    /// </summary>
    public Task DesaprobarAsync(int certificadoId) =>
        TransicionSimpleAsync(certificadoId, "desaprobar el certificado",
            CertificadoValidator.Desaprobar, EstadoCertificado.Cerrado);

    /// <summary>Transición de estado sin snapshot ni chequeos sobre otras filas (Aprobar/Desaprobar).</summary>
    private async Task TransicionSimpleAsync(int certificadoId, string accion,
        Func<EstadoCertificado, string?> regla, EstadoCertificado destino)
    {
        ExigirRol(accion);
        await using var db = await dbFactory.CreateDbContextAsync();

        var cert = await db.Certificados.FirstOrThrowAsync(c => c.Id == certificadoId, "Certificado no encontrado.");

        Validacion.Exigir(regla(cert.Estado));

        cert.Estado = destino;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Elimina un certificado. Solo se admiten borradores (o anulados): un cerrado
    /// integra la cadena de acumulados y debe reabrirse antes de poder eliminarse.
    /// Devuelve false si ya no existía —eliminado por otro usuario— para que la
    /// página informe (patrón bool-false del resto de los módulos).
    /// </summary>
    public async Task<bool> EliminarAsync(int certificadoId)
    {
        ExigirRol("eliminar el certificado");
        await using var db = await dbFactory.CreateDbContextAsync();

        var cert = await db.Certificados.FirstOrDefaultAsync(c => c.Id == certificadoId);
        if (cert is null) return false;

        Validacion.Exigir(CertificadoValidator.Eliminar(cert.Estado, cert.Numero));

        db.Certificados.Remove(cert);
        await db.SaveChangesAsync();
        return true;
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ítems ejecutados en bloques de certificados anteriores CERRADOS de las mismas
    /// estructuras, en una sola query, agrupados por estructura de costos.
    /// </summary>
    private static async Task<ILookup<int, ItemCertificado>> CargarAnterioresAsync(
        AppDbContext db, IReadOnlyCollection<int> idsEstructuras, Certificado certificado)
    {
        // Solo se suman: nunca se modifican (ni en BuildVm ni en Cerrar) → sin tracking.
        var filas = await db.ItemsCertificado.AsNoTracking()
            .Where(ic => idsEstructuras.Contains(ic.CertificadoEstructura.EstructuraCostosId)
                      && ic.CertificadoEstructura.Certificado.ObraId == certificado.ObraId
                      && ic.CertificadoEstructura.Certificado.Numero < certificado.Numero
                      && (ic.CertificadoEstructura.Certificado.Estado == EstadoCertificado.Cerrado
                       || ic.CertificadoEstructura.Certificado.Estado == EstadoCertificado.Aprobado))
            .Select(ic => new { ic.CertificadoEstructura.EstructuraCostosId, Item = ic })
            .ToListAsync();
        return filas.ToLookup(f => f.EstructuraCostosId, f => f.Item);
    }

    private static void PropagateAgrupadores(List<ItemCertificadoVM> items)
    {
        // Propaga sumas desde hojas hacia agrupadores (bottom-up por orden inverso)
        var agrupadores = items.Where(i => i.EsAgrupador).OrderByDescending(i => i.Orden);
        foreach (var agrup in agrupadores)
        {
            var hijos = items.Where(i => i.AgrupadorPadreId == agrup.ItemEstructuraId).ToList();
            agrup.CantidadAnterior = hijos.Sum(h => h.CantidadAnterior);
            agrup.MontoAnterior    = hijos.Sum(h => h.MontoAnterior);
            agrup.CantidadMes      = hijos.Sum(h => h.CantidadMes);
            agrup.MontoMes         = hijos.Sum(h => h.MontoMes);
            agrup.MontoContrato    = hijos.Sum(h => h.MontoContrato ?? 0);
        }
    }
}
