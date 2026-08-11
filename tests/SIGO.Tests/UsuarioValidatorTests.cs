using SIGO.Services.Validaciones;

namespace SIGO.Tests;

/// <summary>
/// Reglas de usuarios, ahora puras en UsuarioValidator: estos tests fijan el invariante
/// "la app nunca queda sin un Admin activo" (quitar rol, desactivar o eliminar al
/// último se rechaza) y el bloqueo de eliminar a un director con obras asignadas.
/// No hay alta manual: los usuarios se registran solos al primer ingreso.
/// </summary>
public class UsuarioValidatorTests
{
    // ── Campos obligatorios (edición) ────────────────────────────────────────────

    [Fact]
    public void Actualizar_ConNombre_Pasa() =>
        Assert.Null(UsuarioValidator.Actualizar("Juan Pérez"));

    [Fact]
    public void Actualizar_NombreEnBlanco_Rechaza() =>
        // Solo espacios: el RequiredValidator del form lo deja pasar y el Trim() lo
        // persistía como cadena vacía.
        Assert.Equal("El nombre es obligatorio", UsuarioValidator.Actualizar("   "));

    [Fact]
    public void Actualizar_NombreNulo_Rechaza() =>
        Assert.Equal("El nombre es obligatorio", UsuarioValidator.Actualizar(null));

    // ── Quitar Admin en la edición (último Admin activo) ─────────────────────────

    [Fact]
    public void QuitarAdmin_AlUltimoAdminActivo_Rechaza() =>
        Assert.Equal("Es el único Admin activo: asigná Admin a otro usuario antes de quitárselo.",
            UsuarioValidator.QuitarAdmin(esAdminActivo: true, seguiraComoAdminActivo: false, hayOtroAdminActivo: false));

    [Fact]
    public void QuitarAdmin_ConOtroAdminActivo_Pasa() =>
        // Queda otro Admin activo: sacarle Admin (o desactivarlo) a este no rompe el invariante.
        Assert.Null(UsuarioValidator.QuitarAdmin(esAdminActivo: true, seguiraComoAdminActivo: false, hayOtroAdminActivo: true));

    [Fact]
    public void QuitarAdmin_SiSigueSiendoAdminActivo_Pasa() =>
        // La edición conserva Admin + Activo: no hay pérdida aunque sea el único.
        Assert.Null(UsuarioValidator.QuitarAdmin(esAdminActivo: true, seguiraComoAdminActivo: true, hayOtroAdminActivo: false));

    [Fact]
    public void QuitarAdmin_SiNoEraAdminActivo_Pasa() =>
        // Editar a alguien que no era Admin activo (sin rol Admin, o inactivo) nunca
        // puede dejar la app sin Admin: no aporta al invariante.
        Assert.Null(UsuarioValidator.QuitarAdmin(esAdminActivo: false, seguiraComoAdminActivo: false, hayOtroAdminActivo: false));

    // ── Eliminar al último Admin activo ──────────────────────────────────────────

    [Fact]
    public void EliminarAdmin_AlUltimoAdminActivo_Rechaza() =>
        Assert.Equal("Es el único Admin activo: asigná Admin a otro usuario antes de eliminarlo.",
            UsuarioValidator.EliminarAdmin(esAdminActivo: true, hayOtroAdminActivo: false));

    [Fact]
    public void EliminarAdmin_ConOtroAdminActivo_Pasa() =>
        Assert.Null(UsuarioValidator.EliminarAdmin(esAdminActivo: true, hayOtroAdminActivo: true));

    [Fact]
    public void EliminarAdmin_SiNoEraAdminActivo_Pasa() =>
        // Un usuario inactivo o sin rol Admin se puede eliminar aunque no haya otro:
        // no cuenta para el invariante (ya no administraba).
        Assert.Null(UsuarioValidator.EliminarAdmin(esAdminActivo: false, hayOtroAdminActivo: false));

    // ── Eliminar a un director con obras ─────────────────────────────────────────

    [Fact]
    public void EliminarDirector_SinObras_Pasa() =>
        Assert.Null(UsuarioValidator.EliminarDirector(obrasComoDirector: 0));

    [Fact]
    public void EliminarDirector_ConObras_RechazaEIndicaCuantas() =>
        Assert.Equal("Es director de 3 obra(s): reasignalas antes de eliminarlo, o desactivalo.",
            UsuarioValidator.EliminarDirector(obrasComoDirector: 3));
}
