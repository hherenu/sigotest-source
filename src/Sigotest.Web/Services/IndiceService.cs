using Microsoft.EntityFrameworkCore;
using SIGO.Data;
using SIGO.Models;
using SIGO.Models.ViewModels;
using SIGO.Services.Validaciones;

namespace SIGO.Services;

/// <summary>
/// Catálogo de índices INDEC, sus valores mensuales y la importación de publicaciones.
/// Las páginas de /indices no tocan el DbContext: leen VMs y mutan a través de estos
/// métodos (mismo patrón que CertificadoService).
/// </summary>
public class IndiceService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser)
{
    /// <summary>Defensa en profundidad (ver <see cref="Roles.Exigir"/>): módulo Certificaciones.</summary>
    private void ExigirRol(string accion) => Roles.Exigir(currentUser.IsInRole, Roles.Certificaciones, accion);

    /// <summary>
    /// Publicación normalizada: trim, y null si viene vacía. ÚNICA implementación
    /// (la usan el alta de valor, la importación y las páginas para armar mensajes):
    /// dos normalizaciones distintas harían que "duplicado" y "guardado" no coincidan.
    /// </summary>
    public static string? NormalizarPublicacion(string? idPublicacion) =>
        string.IsNullOrWhiteSpace(idPublicacion) ? null : idPublicacion.Trim();

    // ── Catálogo de índices ──────────────────────────────────────────────────────

    /// <summary>Proyección entidad → VM, única para listado y detalle.</summary>
    private static readonly System.Linq.Expressions.Expression<Func<IndiceINDEC, IndiceVM>> AVm = i => new IndiceVM
    {
        Id = i.Id,
        CodigoIndice = i.CodigoIndice,
        FamiliaRecurso = i.FamiliaRecurso,
        IndiceNormalizado = i.IndiceNormalizado,
        CuadroReferencia = i.CuadroReferencia,
        IncisoCode = i.IncisoCode
    };

    /// <summary>Catálogo completo ordenado por familia de recurso (grilla de /indices).</summary>
    public async Task<List<IndiceVM>> ListarAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.IndicesINDEC.AsNoTracking()
            .OrderBy(i => i.FamiliaRecurso)
            .Select(AVm)
            .ToListAsync();
    }

    /// <summary>Un índice del catálogo, o null si no existe (la página muestra "no encontrado").</summary>
    public async Task<IndiceVM?> ObtenerAsync(int indiceId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.IndicesINDEC.AsNoTracking()
            .Where(i => i.Id == indiceId)
            .Select(AVm)
            .FirstOrDefaultAsync();
    }

    /// <summary>Alta de un índice en el catálogo. Devuelve el Id generado.</summary>
    public async Task<int> CrearAsync(IndiceVM vm)
    {
        ExigirRol("crear el índice");
        Validacion.Exigir(IndiceValidator.Guardar(vm.CodigoIndice, vm.FamiliaRecurso));
        // El import matchea por código EXACTO: un espacio al inicio deja el índice
        // inalcanzable para siempre ("Código desconocido" en cada pegado).
        var codigo = vm.CodigoIndice.Trim();

        await using var db = await dbFactory.CreateDbContextAsync();
        Validacion.Exigir(IndiceValidator.CodigoDuplicado(
            codigo, await db.IndicesINDEC.AnyAsync(i => i.CodigoIndice == codigo)));

        var indice = new IndiceINDEC
        {
            CodigoIndice = codigo,
            FamiliaRecurso = vm.FamiliaRecurso.Trim(),
            IndiceNormalizado = Limpiar(vm.IndiceNormalizado),
            CuadroReferencia = Limpiar(vm.CuadroReferencia),
            IncisoCode = Limpiar(vm.IncisoCode)
        };
        db.IndicesINDEC.Add(indice);
        await db.SaveChangesAsync();
        return indice.Id;
    }

    /// <summary>Opcionales del alta: trim, y null si vienen vacíos o solo espacios.</summary>
    private static string? Limpiar(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ── Valores mensuales ────────────────────────────────────────────────────────

    /// <summary>Valores mensuales de un índice, del período más reciente al más viejo.</summary>
    public async Task<List<ValorIndiceVM>> ListarValoresAsync(int indiceId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ValoresIndice.AsNoTracking()
            .Where(v => v.IndiceId == indiceId)
            .OrderByDescending(v => v.Anio).ThenByDescending(v => v.Mes)
            .Select(v => new ValorIndiceVM
            {
                Id = v.Id,
                Anio = v.Anio,
                Mes = v.Mes,
                Valor = v.Valor,
                IdPublicacion = v.IdPublicacion,
                RowVersion = v.RowVersion
            })
            .ToListAsync();
    }

    /// <summary>
    /// Agrega un valor mensual. Devuelve false si ya existe uno para el mismo
    /// índice/período dentro de la misma publicación (la página avisa "Duplicado"
    /// sin tratarlo como error). Misma regla que los índices únicos de la DB: el
    /// mismo mes puede repetirse bajo publicaciones distintas (revisiones de
    /// revistas posteriores); solo es duplicado dentro de la misma publicación
    /// (o entre valores sin publicación).
    /// </summary>
    public async Task<bool> AgregarValorAsync(int indiceId, NuevoValorVM nuevo)
    {
        ExigirRol("agregar el valor");
        Validacion.Exigir(IndiceValidator.ValorMensual(nuevo.Valor));
        var pub = NormalizarPublicacion(nuevo.IdPublicacion);
        await using var db = await dbFactory.CreateDbContextAsync();

        if (await db.ValoresIndice.AnyAsync(v =>
                v.IndiceId == indiceId && v.Anio == nuevo.Anio && v.Mes == nuevo.Mes && v.IdPublicacion == pub))
            return false;

        db.ValoresIndice.Add(new ValorIndice
        {
            IndiceId = indiceId,
            Anio = nuevo.Anio,
            Mes = nuevo.Mes,
            Valor = nuevo.Valor!.Value,
            IdPublicacion = pub
        });
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Elimina un valor mensual. Devuelve false si ya no existe ("ya eliminado por
    /// otro usuario", mismo patrón que RedeterminacionService.EliminarDisparoAsync).
    /// El token de la grilla viaja al DELETE: si otro usuario lo modificó desde que
    /// la página lo mostró, salta el conflicto de concurrencia.
    /// </summary>
    public async Task<bool> EliminarValorAsync(int valorId, byte[] rowVersion)
    {
        ExigirRol("eliminar el valor");
        await using var db = await dbFactory.CreateDbContextAsync();

        var valor = await db.ValoresIndice.FirstOrDefaultAsync(v => v.Id == valorId);
        if (valor is null) return false;

        db.Entry(valor).Property(v => v.RowVersion).OriginalValue = rowVersion;

        db.ValoresIndice.Remove(valor);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Importación de publicaciones ─────────────────────────────────────────────

    /// <summary>
    /// Análisis previo a la importación: valida cada fila parseada por
    /// <see cref="ImportadorIndec"/> contra el catálogo (código inexistente → rechazada),
    /// contra el propio pegado (mismo código y período repetido) y contra la base
    /// (duplicado dentro de la misma publicación → se omite). Muta Estado/Detalle/IndiceId
    /// de las filas: la página muestra la preview con esto ANTES de confirmar, sin tocar
    /// el DbContext. Solo lectura: no exige rol.
    /// </summary>
    public async Task AnalizarImportacionAsync(List<FilaImport> filas, string? idPublicacion)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var codigos = await db.IndicesINDEC.AsNoTracking()
            .ToDictionaryAsync(i => i.CodigoIndice, i => i.Id, StringComparer.OrdinalIgnoreCase);
        var pub = NormalizarPublicacion(idPublicacion);

        // Duplicados dentro del propio pegado (mismo código y período repetido).
        var vistos = new HashSet<(int, int, int)>();

        // Misma regla que los índices únicos de la DB: solo es duplicado dentro de la
        // misma publicación (o entre valores sin publicación). Se trae la publicación
        // completa en una query (antes era un AnyAsync por fila pegada).
        var existentes = (await db.ValoresIndice.AsNoTracking()
                .Where(v => v.IdPublicacion == pub)
                .Select(v => new { v.IndiceId, v.Anio, v.Mes })
                .ToListAsync())
            .Select(v => (v.IndiceId, v.Anio, v.Mes))
            .ToHashSet();

        foreach (var f in filas.Where(f => f.Estado == EstadoFilaImport.Ok))
        {
            if (!codigos.TryGetValue(f.Codigo, out var indiceId))
            {
                f.Estado = EstadoFilaImport.CodigoDesconocido;
                f.Detalle = "No existe en el catálogo de índices.";
                continue;
            }
            f.IndiceId = indiceId;

            if (!vistos.Add((indiceId, f.Anio, f.Mes)))
            {
                f.Estado = EstadoFilaImport.DuplicadaEnPegado;
                f.Detalle = "El mismo código y período aparece más arriba.";
                continue;
            }

            if (existentes.Contains((indiceId, f.Anio, f.Mes)))
                f.Estado = EstadoFilaImport.YaExiste;
        }
    }

    /// <summary>
    /// Importa las filas en estado Ok del análisis previo bajo la publicación dada.
    /// Devuelve la cantidad insertada. Un solo SaveChanges: la publicación entra
    /// completa o no entra (si otra sesión importó lo mismo, el índice único corta todo).
    /// </summary>
    public async Task<int> ImportarAsync(IEnumerable<FilaImport> filas, string? idPublicacion)
    {
        ExigirRol("importar la publicación");
        var pub = NormalizarPublicacion(idPublicacion);
        var aImportar = filas.Where(f => f.Estado == EstadoFilaImport.Ok).ToList();
        if (aImportar.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync();
        db.ValoresIndice.AddRange(aImportar.Select(f => new ValorIndice
        {
            IndiceId = f.IndiceId,
            Anio = f.Anio,
            Mes = f.Mes,
            Valor = f.Valor!.Value,
            IdPublicacion = pub
        }));
        await db.SaveChangesAsync();
        return aImportar.Count;
    }
}
