using SIGO.Models.Enums;
using SIGO.Services.Validaciones;

namespace SIGO.Tests;

/// <summary>
/// Reglas del ciclo de vida del certificado, ahora puras en CertificadoValidator:
/// estos tests fijan el orden de cierre (sin huecos, anteriores cerrados), las
/// dependencias de reapertura y la numeración del alta.
/// </summary>
public class CertificadoValidatorTests
{
    private static (int, EstadoCertificado) Cerrado(int n) => (n, EstadoCertificado.Cerrado);
    private static (int, EstadoCertificado) Borrador(int n) => (n, EstadoCertificado.Borrador);

    // ── Cerrar ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Cerrar_ConAnterioresCerrados_Pasa() =>
        Assert.Null(CertificadoValidator.Cerrar(EstadoCertificado.Borrador, 3, [Cerrado(1), Cerrado(2)]));

    [Fact]
    public void Cerrar_ElPrimero_Pasa() =>
        Assert.Null(CertificadoValidator.Cerrar(EstadoCertificado.Borrador, 1, []));

    [Fact]
    public void Cerrar_FueraDeBorrador_Rechaza() =>
        Assert.Equal("Solo se puede cerrar un certificado en borrador.",
            CertificadoValidator.Cerrar(EstadoCertificado.Cerrado, 1, []));

    [Fact]
    public void Cerrar_ConHueco_RechazaEIndicaElFaltante()
    {
        // Existe el 1 pero no el 2: cerrar el 3 congelaría un "Anterior" incompleto.
        var error = CertificadoValidator.Cerrar(EstadoCertificado.Borrador, 3, [Cerrado(1)]);
        Assert.Contains("no existe el N° 2", error);
    }

    [Fact]
    public void Cerrar_ConAnteriorEnBorrador_RechazaEIndicaElNumero()
    {
        var error = CertificadoValidator.Cerrar(EstadoCertificado.Borrador, 3, [Cerrado(1), Borrador(2)]);
        Assert.Contains("el N° 2 sigue en borrador", error);
    }

    [Fact]
    public void Cerrar_ReportaElMenorDeLosAbiertos()
    {
        var error = CertificadoValidator.Cerrar(EstadoCertificado.Borrador, 4, [Borrador(1), Cerrado(2), Borrador(3)]);
        Assert.Contains("el N° 1", error);
    }

    // ── Reabrir ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Reabrir_SinPosterioresCerrados_Pasa() =>
        Assert.Null(CertificadoValidator.Reabrir(EstadoCertificado.Cerrado, 2, []));

    [Fact]
    public void Reabrir_FueraDeCerrado_Rechaza() =>
        Assert.Equal("Solo se puede reabrir un certificado cerrado.",
            CertificadoValidator.Reabrir(EstadoCertificado.Aprobado, 2, []));

    [Fact]
    public void Reabrir_ConPosteriorCerrado_RechazaEIndicaElMenor()
    {
        // El snapshot del 3 (y del 4) congeló su "Anterior" incluyendo al 2.
        var error = CertificadoValidator.Reabrir(EstadoCertificado.Cerrado, 2, [4, 3]);
        Assert.Contains("el N° 3", error);
    }

    // ── Transiciones simples ─────────────────────────────────────────────────────

    [Fact]
    public void Guardar_SoloEnBorrador()
    {
        Assert.Null(CertificadoValidator.Guardar(EstadoCertificado.Borrador));
        Assert.NotNull(CertificadoValidator.Guardar(EstadoCertificado.Cerrado));
    }

    [Fact]
    public void Aprobar_SoloDesdeCerrado()
    {
        Assert.Null(CertificadoValidator.Aprobar(EstadoCertificado.Cerrado));
        Assert.NotNull(CertificadoValidator.Aprobar(EstadoCertificado.Borrador));
    }

    [Fact]
    public void Desaprobar_SoloDesdeAprobado()
    {
        Assert.Null(CertificadoValidator.Desaprobar(EstadoCertificado.Aprobado));
        Assert.NotNull(CertificadoValidator.Desaprobar(EstadoCertificado.Cerrado));
    }

    [Fact]
    public void Eliminar_RechazaCerradosYAprobados()
    {
        Assert.Null(CertificadoValidator.Eliminar(EstadoCertificado.Borrador, 1));
        Assert.NotNull(CertificadoValidator.Eliminar(EstadoCertificado.Cerrado, 1));
        Assert.NotNull(CertificadoValidator.Eliminar(EstadoCertificado.Aprobado, 1));
    }

    // ── Numeración del alta ──────────────────────────────────────────────────────

    [Fact]
    public void NumeroNuevo_Consecutivo_Pasa() =>
        Assert.Null(CertificadoValidator.NumeroNuevo(3, yaExiste: false, maxNumero: 2, ultimoCerrado: 2));

    [Fact]
    public void NumeroNuevo_Duplicado_Rechaza() =>
        Assert.Equal("Ya existe un certificado con ese número para esta obra.",
            CertificadoValidator.NumeroNuevo(2, yaExiste: true, maxNumero: 2, ultimoCerrado: 0));

    [Fact]
    public void NumeroNuevo_ConHuecoHaciaArriba_Rechaza()
    {
        var error = CertificadoValidator.NumeroNuevo(5, yaExiste: false, maxNumero: 2, ultimoCerrado: 0);
        Assert.Contains("el próximo número es 3", error);
    }

    [Fact]
    public void NumeroNuevo_PorDebajoDelUltimoCerrado_Rechaza()
    {
        // El 1 fue eliminado y el 2 ya cerró: recrear el 1 quedaría fuera del acumulado congelado.
        var error = CertificadoValidator.NumeroNuevo(1, yaExiste: false, maxNumero: 2, ultimoCerrado: 2);
        Assert.Contains("último certificado cerrado (N° 2)", error);
    }

    // ── Bloques (agregar / quitar) ───────────────────────────────────────────────

    [Fact]
    public void AgregarBloque_EnBorradorSinDuplicadoYDeLaObra_Pasa() =>
        Assert.Null(CertificadoValidator.AgregarBloque(
            EstadoCertificado.Borrador, bloqueDuplicado: false, estructuraDeLaObra: true));

    [Fact]
    public void AgregarBloque_FueraDeBorrador_Rechaza() =>
        Assert.Equal("Solo se pueden agregar bloques en borrador.",
            CertificadoValidator.AgregarBloque(
                EstadoCertificado.Cerrado, bloqueDuplicado: false, estructuraDeLaObra: true));

    [Fact]
    public void AgregarBloque_Duplicado_Rechaza() =>
        Assert.Equal("Ese bloque ya está en el certificado.",
            CertificadoValidator.AgregarBloque(
                EstadoCertificado.Borrador, bloqueDuplicado: true, estructuraDeLaObra: true));

    [Fact]
    public void AgregarBloque_EstructuraDeOtraObra_Rechaza() =>
        Assert.Equal("Estructura inválida.",
            CertificadoValidator.AgregarBloque(
                EstadoCertificado.Borrador, bloqueDuplicado: false, estructuraDeLaObra: false));

    [Fact]
    public void AgregarBloque_ElEstadoSeChequeaPrimero()
    {
        // Fuera de borrador ninguna otra condición importa: es el mismo orden que
        // evaluaba la página (else-if) y el que la UI usa para elegir el título.
        Assert.Equal("Solo se pueden agregar bloques en borrador.",
            CertificadoValidator.AgregarBloque(
                EstadoCertificado.Cerrado, bloqueDuplicado: true, estructuraDeLaObra: false));
    }

    [Fact]
    public void QuitarBloque_SoloEnBorrador()
    {
        Assert.Null(CertificadoValidator.QuitarBloque(EstadoCertificado.Borrador));
        Assert.Equal("Solo se pueden quitar bloques en borrador.",
            CertificadoValidator.QuitarBloque(EstadoCertificado.Cerrado));
        Assert.Equal("Solo se pueden quitar bloques en borrador.",
            CertificadoValidator.QuitarBloque(EstadoCertificado.Aprobado));
    }
}
