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
    public static async Task<bool> EjecutarAsync(Func<Task> accion, NotificationService notif)
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
                "Otro usuario modificó este registro mientras trabajabas. Recargá la página y volvé a intentar.", 8000);
        }
        catch (DbUpdateException ex)
        {
            // Los códigos frecuentes de SQL Server se traducen a un mensaje entendible
            // en vez del error crudo del proveedor en inglés.
            var msg = (ex.InnerException as Microsoft.Data.SqlClient.SqlException)?.Number switch
            {
                547 => "No se puede completar: el registro está referenciado por otros datos "
                     + "(certificados, redeterminaciones, planes o ítems que dependen de él).",
                2601 or 2627 => "Ya existe un registro con esa clave (valor duplicado).",
                _ => ex.InnerException?.Message ?? ex.Message
            };
            notif.Notify(NotificationSeverity.Error, "Error al guardar", msg, 8000);
        }
        // Fallos del proveedor fuera de SaveChanges (SqlException en una query o al
        // abrir la conexión): sin esto, un corte transitorio de la DB dentro de la
        // acción tumbaba el circuito pese al wrapper.
        catch (System.Data.Common.DbException ex)
        {
            notif.Notify(NotificationSeverity.Error, "Error de base de datos",
                $"No se pudo acceder a la base de datos. Reintentá en unos segundos. ({ex.Message})", 8000);
        }
        // Reglas de negocio de los servicios (ej. "solo se puede cerrar un borrador").
        catch (InvalidOperationException ex)
        {
            notif.Notify(NotificationSeverity.Error, "No se pudo completar", ex.Message, 6000);
        }
        return false;
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
    /// <summary>Cantidad "1.234,56" o "" si es 0.</summary>
    public static string Qty(decimal v) => v != 0 ? v.ToString("N2", EsAr) : "";
    /// <summary>Número con 2 decimales.</summary>
    public static string N2(decimal v) => v.ToString("N2", EsAr);
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
}
