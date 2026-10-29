using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Radzen;
using SIGO.Models.Enums;
using SIGO.Services;

namespace SIGO.Components.Shared;

/// <summary>
/// Ejecuta operaciones de escritura de forma segura: traduce los fallos esperables
/// (conflicto de concurrencia por RowVersion, error de base, regla de negocio de un
/// servicio) en una notificación al usuario en vez de tumbar el circuito de Blazor.
/// </summary>
public static class Persistencia
{
    /// <summary>Devuelve true solo si la operación se completó sin errores.</summary>
    /// <param name="conflicto">
    /// Mensaje propio para un conflicto de concurrencia. Las páginas que tras un fallo
    /// releen conservando lo tipeado (carga de certificado, planificación) no pueden decir
    /// «Recargá la página»: recargar borra justamente lo que se conservó.
    /// </param>
    public static async Task<bool> EjecutarAsync(Func<Task> accion, NotificationService notif, string? conflicto = null)
    {
        try
        {
            await accion();
            return true;
        }
        // DbUpdateConcurrencyException hereda de DbUpdateException: va primero.
        catch (DbUpdateConcurrencyException)
        {
            notif.Notify(NotificationSeverity.Warning, "Conflicto de concurrencia",
                conflicto ?? "Otro usuario modificó este registro mientras trabajabas. Recargá la página y volvé a intentar.", 8000);
        }
        catch (DbUpdateException ex)
        {
            NotificarErrorDeBase(ex, notif);
        }
        // Fallos del proveedor fuera de SaveChanges (SqlException en una query o al
        // abrir la conexión): sin esto, un corte transitorio de la DB dentro de la
        // acción tumbaba el circuito pese al wrapper.
        catch (System.Data.Common.DbException ex)
        {
            NotificarErrorDeBase(ex, notif);
        }
        // Sin reintentos configurados, EF envuelve los errores transitorios de SQL Server
        // (un deadlock entre transacciones Serializable, un timeout) en una
        // InvalidOperationException con un mensaje en inglés: no es una regla de negocio,
        // se traduce como el error de base que lleva adentro.
        catch (InvalidOperationException ex) when (ex.InnerException is DbUpdateException or System.Data.Common.DbException)
        {
            NotificarErrorDeBase(ex.InnerException, notif);
        }
        // Reglas de negocio de los servicios (ej. "solo se puede cerrar un borrador").
        catch (InvalidOperationException ex)
        {
            notif.Notify(NotificationSeverity.Error, "No se pudo completar", ex.Message, 6000);
        }
        return false;
    }

    /// <summary>
    /// Mensaje del error de base: los códigos frecuentes de SQL Server se traducen a un
    /// mensaje entendible en vez del error crudo del proveedor en inglés.
    /// </summary>
    private static void NotificarErrorDeBase(Exception ex, NotificationService notif)
    {
        // Las transacciones Serializable (altas con número correlativo, cierres,
        // importación) eligen una víctima cuando dos guardan a la vez sobre las mismas
        // filas: la operación no se hizo y alcanza con repetirla.
        const string Deadlock = "Otro usuario estaba guardando datos relacionados en el mismo momento. Volvé a intentar.";

        if (ex is DbUpdateException)
        {
            var msg = (ex.InnerException as Microsoft.Data.SqlClient.SqlException)?.Number switch
            {
                547 => "No se puede completar: el registro está referenciado por otros datos "
                     + "(certificados, redeterminaciones, planes o ítems que dependen de él).",
                2601 or 2627 => "Ya existe un registro con esa clave (valor duplicado).",
                1205 => Deadlock,
                _ => ex.InnerException?.Message ?? ex.Message
            };
            notif.Notify(NotificationSeverity.Error, "Error al guardar", msg, 8000);
            return;
        }

        notif.Notify(NotificationSeverity.Error, "Error de base de datos",
            (ex as Microsoft.Data.SqlClient.SqlException)?.Number == 1205
                ? Deadlock
                : $"No se pudo acceder a la base de datos. Reintentá en unos segundos. ({ex.Message})", 8000);
    }
}

/// <summary>
/// Nombres de meses y formateo de montos / publicaciones INDEC compartido por las páginas Blazor.
/// Centraliza lo que antes estaba duplicado en cada componente.
/// </summary>
public static class Fmt
{
    private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");

    private static readonly string[] MesesNombres =
        { "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre" };
    private static readonly string[] MesesCortos =
        { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };
    private static readonly string[] MesesMayus =
        { "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE" };

    /// <summary>Nombre completo del mes (1-12). Ej: "Marzo".</summary>
    public static string MesNombre(int mes) => MesesNombres[mes - 1];
    /// <summary>Nombre del mes con índice acotado: datos sucios no tiran excepción (exports de controllers).</summary>
    public static string MesNombreSeguro(int mes) => mes is >= 1 and <= 12 ? MesesNombres[mes - 1] : $"Mes {mes}";
    /// <summary>Abreviatura del mes (1-12). Ej: "Mar".</summary>
    public static string MesCorto(int mes) => MesesCortos[mes - 1];

    // Cultura es-AR EXPLÍCITA en todos los formatos (antes Money/Qty/N2 usaban la
    // del servidor y Pesos la fijaba): un cambio de cultura del host alteraba los
    // separadores de la mitad de las tablas y no de la otra.
    /// <summary>"$" + monto con 2 decimales, o "" si es 0.</summary>
    public static string Money(decimal v) => v != 0 ? "$ " + v.ToString("N2", EsAr) : "";
    /// <summary>Cantidad con <see cref="HastaN4"/> ("1.234,56", "115,3488") o "" si es 0.</summary>
    public static string Qty(decimal v) => v != 0 ? HastaN4(v) : "";
    /// <summary>Número con 2 decimales.</summary>
    public static string N2(decimal v) => v.ToString("N2", EsAr);
    /// <summary>Número con 4 decimales fijos.</summary>
    public static string N4(decimal v) => v.ToString("N4", EsAr);
    /// <summary>
    /// Cantidades y precios unitarios: 2 decimales como mínimo y hasta los 4 que guarda la
    /// base ("1,00", "115,3488", "1.317.533,4246"). Con 2 fijos el detalle mostraba 115,35 y
    /// cantidad × PU no daba el monto de la fila; con 4 fijos casi todas las filas llevaban
    /// ceros de más. Los montos siguen con 2 (N2/Money).
    /// </summary>
    public static string HastaN4(decimal v) => v.ToString("#,##0.00##", EsAr);
    /// <summary>Monto con separadores es-AR (grilla de planificación).</summary>
    public static string Pesos(decimal v) => v.ToString("N2", EsAr);

    /// <summary>Símbolo de moneda.</summary>
    public static string Simbolo(Moneda m) => m switch { Moneda.Pesos => "$", Moneda.USD => "US$", Moneda.EUR => "€", _ => "" };

    // ── Publicaciones INDEC Informa ──────────────────────────────────────────────
    /// <summary>"INDEC_INFORMA_MM_AA" → "Rev. MM/AA". ÚNICA implementación del reemplazo.</summary>
    public static string PubRevista(string pub) => pub.Replace("INDEC_INFORMA_", "Rev. ").Replace("_", "/");
    /// <summary>Etiqueta "Rev. MM/AA — Mes Año" (vista Cálculo guardado).</summary>
    public static string PubLabelRevista(string? pub, int mes, int anio) =>
        pub is not null ? $"{PubRevista(pub)} — {MesCorto(mes)} {anio}" : $"{MesCorto(mes)} {anio}";
    /// <summary>"MES/20AA" en mayúsculas (historial). "—" si es null.</summary>
    public static string PubMayus(string? pub)
    {
        if (pub is null) return "—";
        // El parseo del sufijo vive en Services.PublicacionIndec (única implementación,
        // compartida con el orden cronológico de las publicaciones).
        return PublicacionIndec.TryPeriodo(pub, out var anio, out var mes)
            ? $"{MesesMayus[mes - 1]}/{anio}"
            : pub.Replace("_", " ");
    }
}

/// <summary>Reglas del salto de redeterminación (la variación ≥ 4% habilita el trámite).</summary>
public static class Redet
{
    public const decimal SaltoMinimo = 4m;

    public static bool EsSuba(decimal? porcentaje) => porcentaje.GetValueOrDefault() >= SaltoMinimo;
    /// <summary>Color Radzen según supere o no el umbral (verde/rojo).</summary>
    public static string ColorSuba(decimal? porcentaje) => EsSuba(porcentaje) ? "var(--rz-success)" : "var(--rz-danger)";
}

/// <summary>
/// Patrón estándar de las acciones con confirmación de toda la app, en un solo lugar
/// (antes estaba copiado casi carácter por carácter en 9 páginas): Confirm de Radzen →
/// Persistencia → Notify de éxito. La página conserva el flag `procesando` (seteado
/// ANTES de llamar — un doble click no debe apilar dos diálogos) y la recarga posterior.
/// Los pre-checks de negocio van DENTRO de `accion` lanzando InvalidOperationException:
/// Persistencia ya la traduce a una notificación con el mensaje.
/// </summary>
public static class UiAcciones
{
    /// <summary>Devuelve true solo si el usuario confirmó y la acción se completó sin errores.</summary>
    public static async Task<bool> ConfirmarYEjecutarAsync(
        DialogService dialogos, NotificationService notif,
        string pregunta, string tituloConfirmacion, string textoOk,
        Func<Task> accion, string tituloExito, string detalleExito)
    {
        var ok = await dialogos.Confirm(pregunta, tituloConfirmacion,
            new ConfirmOptions { OkButtonText = textoOk, CancelButtonText = "Cancelar" });
        if (ok != true) return false;

        if (!await Persistencia.EjecutarAsync(accion, notif)) return false;

        notif.Notify(NotificationSeverity.Success, tituloExito, detalleExito, 3000);
        return true;
    }

    /// <summary>
    /// Variante para las eliminaciones con patrón bool-false ("ya eliminado por otro
    /// usuario"): el servicio devuelve false si el registro ya no existía y ese caso
    /// se informa como Info, no como éxito. La página recarga en Eliminado y en
    /// YaEliminado (la grilla quedó desactualizada), no en Cancelado ni Error.
    /// </summary>
    public static async Task<ResultadoEliminacion> ConfirmarYEliminarAsync(
        DialogService dialogos, NotificationService notif,
        string pregunta, Func<Task<bool>> eliminar,
        string tituloExito, string detalleExito,
        string tituloYaEliminado, string detalleYaEliminado)
    {
        var ok = await dialogos.Confirm(pregunta, "Confirmar eliminación",
            new ConfirmOptions { OkButtonText = "Eliminar", CancelButtonText = "Cancelar" });
        if (ok != true) return ResultadoEliminacion.Cancelado;

        var existia = false;
        if (!await Persistencia.EjecutarAsync(async () => existia = await eliminar(), notif))
            return ResultadoEliminacion.Error;

        if (!existia)
        {
            notif.Notify(NotificationSeverity.Info, tituloYaEliminado, detalleYaEliminado, 5000);
            return ResultadoEliminacion.YaEliminado;
        }
        notif.Notify(NotificationSeverity.Success, tituloExito, detalleExito, 3000);
        return ResultadoEliminacion.Eliminado;
    }

    /// <summary>
    /// <see cref="ConfirmarYEliminarAsync"/> + la recarga que toda lista hace después:
    /// en Eliminado y YaEliminado (la grilla quedó desactualizada), no en Cancelado ni
    /// Error. `antesDeRecargar` cubre los extras de la página (ej. cerrar el form si se
    /// eliminó el registro en edición).
    /// </summary>
    public static async Task<ResultadoEliminacion> ConfirmarEliminarYRecargarAsync(
        DialogService dialogos, NotificationService notif,
        string pregunta, Func<Task<bool>> eliminar,
        string tituloExito, string detalleExito,
        string tituloYaEliminado, string detalleYaEliminado,
        Func<Task> recargar, Action? antesDeRecargar = null)
    {
        var res = await ConfirmarYEliminarAsync(dialogos, notif, pregunta, eliminar,
            tituloExito, detalleExito, tituloYaEliminado, detalleYaEliminado);
        if (res is not (ResultadoEliminacion.Eliminado or ResultadoEliminacion.YaEliminado)) return res;
        antesDeRecargar?.Invoke();
        await recargar();
        return res;
    }

    /// <summary>
    /// Guardado estándar de los forms: Persistencia → Notify de éxito → navegación.
    /// La página conserva el flag `procesando` alrededor. El destino es un Func porque
    /// puede depender del resultado del guardado (ej. el id de un alta).
    /// </summary>
    public static async Task GuardarYNavegarAsync(
        NotificationService notif, NavigationManager nav,
        Func<Task> guardar, string tituloExito, Func<string> detalleExito, Func<string> destino)
    {
        if (!await Persistencia.EjecutarAsync(guardar, notif)) return;
        notif.Notify(NotificationSeverity.Success, tituloExito, detalleExito(), 3000);
        nav.NavigateTo(destino());
    }
}

/// <summary>
/// Foto de un formulario para saber si tiene cambios sin guardar (GuardNavegacion): se
/// compara la serialización JSON del modelo entero, sin listar campo por campo (un campo
/// nuevo en el VM queda cubierto solo).
/// </summary>
public sealed class Instantanea<T> where T : class
{
    private string? foto;

    /// <summary>Toma la foto (al cargar el formulario o tras guardarlo).</summary>
    public void Tomar(T modelo) => foto = System.Text.Json.JsonSerializer.Serialize(modelo);

    /// <summary>true si el modelo difiere de la foto. Sin foto o sin modelo, no hay cambios.</summary>
    public bool Cambio(T? modelo) =>
        modelo is not null && foto is not null && System.Text.Json.JsonSerializer.Serialize(modelo) != foto;
}

/// <summary>Resultado de <see cref="UiAcciones.ConfirmarYEliminarAsync"/>.</summary>
public enum ResultadoEliminacion { Cancelado, Eliminado, YaEliminado, Error }

/// <summary>Mapeo de estados de dominio a estilos de badge de Radzen.</summary>
public static class EstadoUi
{
    /// <summary>Badge del estado computado de la obra ("Proceso Licitatorio"/"Vigente"/"Plazo Vencido"/"Proyectada").</summary>
    public static BadgeStyle BadgeObra(string? estado) => estado switch
    {
        "Plazo Vencido" => BadgeStyle.Danger,
        "Proceso Licitatorio" => BadgeStyle.Info,
        "Proyectada" => BadgeStyle.Secondary,
        _ => BadgeStyle.Success
    };

    public static BadgeStyle Badge(EstadoCertificado e) => e switch
    {
        EstadoCertificado.Borrador => BadgeStyle.Secondary,
        EstadoCertificado.Cerrado => BadgeStyle.Success,
        EstadoCertificado.Aprobado => BadgeStyle.Primary,
        EstadoCertificado.Anulado => BadgeStyle.Danger,
        _ => BadgeStyle.Secondary
    };

    public static BadgeStyle Badge(EstadoPlanificacion e) => e switch
    {
        EstadoPlanificacion.Pendiente => BadgeStyle.Secondary,
        EstadoPlanificacion.Cargada => BadgeStyle.Info,
        EstadoPlanificacion.EnRevision => BadgeStyle.Warning,
        EstadoPlanificacion.Aprobada => BadgeStyle.Success,
        _ => BadgeStyle.Secondary
    };

    public static BadgeStyle Badge(EstadoRedeterminacion e) => e switch
    {
        EstadoRedeterminacion.Presentada => BadgeStyle.Info,
        EstadoRedeterminacion.AprobadaCCyR or EstadoRedeterminacion.AprobadaOS => BadgeStyle.Success,
        EstadoRedeterminacion.Rechazada or EstadoRedeterminacion.Anulada => BadgeStyle.Danger,
        _ => BadgeStyle.Secondary
    };
}

/// <summary>Aspecto de las filas de ítems de estructura en las RadzenDataGrid (ítems y vista previa de la importación).</summary>
public static class FilaEstructuraUi
{
    /// <summary>Estilo del RowRender de un agrupador: fondo de rubro (nivel 0) o sub-rubro, en negrita.</summary>
    public static string EstiloAgrupador(int nivel) =>
        $"background:{(nivel == 0 ? "var(--app-agrup-0-bg)" : "var(--app-agrup-1-bg)")};font-weight:600";

    /// <summary>Sangría en px del texto de una fila según su nivel en el árbol.</summary>
    public static int Sangria(int nivel) => nivel * 16 + 8;
}

/// <summary>Aplana un árbol auto-referencial (padre → hijos) en orden de visualización, con el nivel de anidamiento.</summary>
public static class Arbol
{
    public static List<(T Item, int Nivel)> Aplanar<T>(
        IReadOnlyCollection<T> items,
        Func<T, int?> padreId, Func<T, int> id, Func<T, int> orden, Func<T, bool> esAgrupador)
    {
        var flat = new List<(T, int)>();
        void Build(int? parent, int nivel)
        {
            foreach (var h in items.Where(i => padreId(i) == parent).OrderBy(orden))
            {
                flat.Add((h, nivel));
                if (esAgrupador(h)) Build(id(h), nivel + 1);
            }
        }
        Build(null, 0);
        return flat;
    }

    /// <summary>
    /// Suma de un valor de las hojas para cada agrupador, contando también las que cuelgan
    /// de sus sub-agrupadores: un rubro con sub-rubros suma todo lo que tiene debajo (antes
    /// solo sus hojas directas, y quedaba en blanco). Cada hoja suma a toda su cadena de
    /// padres; un ciclo en los datos corta la subida en vez de colgar la página.
    /// </summary>
    public static Dictionary<int, decimal> SumaPorAgrupador<T>(
        IReadOnlyCollection<T> items,
        Func<T, int?> padreId, Func<T, int> id, Func<T, bool> esAgrupador, Func<T, decimal> valor)
    {
        var padres = items.ToDictionary(id, padreId);
        var suma = new Dictionary<int, decimal>();
        foreach (var hoja in items.Where(i => !esAgrupador(i)))
        {
            var v = valor(hoja);
            var vistos = new HashSet<int>();
            for (var p = padreId(hoja); p is int pid && vistos.Add(pid); p = padres.GetValueOrDefault(pid))
                suma[pid] = suma.GetValueOrDefault(pid) + v;
        }
        return suma;
    }
}
