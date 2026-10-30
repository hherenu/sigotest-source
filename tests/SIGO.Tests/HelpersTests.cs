using SIGO.Components.Shared;
using SIGO.Services;

namespace SIGO.Tests;

/// <summary>Helpers puros compartidos: roles, correos del director, mails y meses.</summary>
public class HelpersTests
{
    [Fact]
    public void TieneAlguno_MatcheaContraLaListaSeparadaPorComas()
    {
        Assert.True(Roles.TieneAlguno(r => r == Roles.CCyR, Roles.Certificaciones));
        Assert.True(Roles.TieneAlguno(r => r == Roles.Admin, Roles.Certificaciones));
        Assert.False(Roles.TieneAlguno(r => r == Roles.Director, Roles.Certificaciones));
    }

    [Fact]
    public void SoloDirector_ExigeDirectorSinRolesDeAlcanceTotal()
    {
        Assert.True(Roles.SoloDirector(r => r == Roles.Director));
        Assert.False(Roles.SoloDirector(r => r is Roles.Director or Roles.Gerente));
        Assert.False(Roles.SoloDirector(r => r == Roles.Admin));
    }

    [Fact]
    public void CorreosDirector_ElUsuarioAsignadoGanaAlTextoLibre()
    {
        Assert.Equal(["dir@sbase.com.ar"],
            UsuariosNotificationRecipients.CorreosDirector("dir@sbase.com.ar", "otro@x.com, mas@x.com"));
    }

    [Fact]
    public void CorreosDirector_SinUsuario_UsaElTextoLibreSeparadoPorComas()
    {
        Assert.Equal(["a@x.com", "b@x.com"],
            UsuariosNotificationRecipients.CorreosDirector(null, " a@x.com ,, b@x.com "));
        Assert.Empty(UsuariosNotificationRecipients.CorreosDirector("   ", null));
    }

    [Fact]
    public void UrlPlan_ComponeConBaseUrlYDevuelveNullSinElla()
    {
        Assert.Equal("http://srv:5056/planificacion/7/editar", PlanMails.UrlPlan("http://srv:5056/", 7));
        Assert.Null(PlanMails.UrlPlan(null, 7));
        Assert.Null(PlanMails.UrlPlan("  ", 7));
    }

    [Theory]
    [InlineData(1.0, "1,00")]
    [InlineData(6.5, "6,50")]
    [InlineData(2.125, "2,125")]
    [InlineData(115.3488, "115,3488")]          // antes el detalle mostraba 115,35
    [InlineData(1317533.4246, "1.317.533,4246")]
    public void HastaN4_DosDecimalesComoMinimoYHastaCuatro(double valor, string esperado) =>
        Assert.Equal(esperado, Fmt.HastaN4((decimal)valor));

    [Fact]
    public void Qty_UsaHastaCuatroDecimalesYDejaVacioElCero()
    {
        Assert.Equal("115,3488", Fmt.Qty(115.3488m));
        Assert.Equal("", Fmt.Qty(0m));
    }

    // ── Persistencia ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Persistencia_ErrorDeBaseEnvueltoPorEF_SeInformaComoErrorDeBase()
    {
        // Sin reintentos configurados, EF envuelve los errores transitorios (deadlock,
        // timeout) en una InvalidOperationException en inglés: no es una regla de negocio y
        // se informa el error de base que lleva adentro (el deadlock real lo prueba
        // EstructuraServiceTests.AgregarItem_AltasSimultaneas_NoRepitenElOrden).
        var notif = new Radzen.NotificationService();
        var envuelto = new InvalidOperationException("An exception has been raised that is likely due to a transient failure.",
            new Microsoft.EntityFrameworkCore.DbUpdateException("x", new Exception("detalle del proveedor")));

        Assert.False(await Persistencia.EjecutarAsync(() => throw envuelto, notif));

        var aviso = Assert.Single(notif.Messages);
        Assert.Equal("Error al guardar", aviso.Summary);
        Assert.Equal("detalle del proveedor", aviso.Detail);
    }

    [Fact]
    public async Task Persistencia_ReglaDeNegocio_SeInformaConSuMensaje()
    {
        var notif = new Radzen.NotificationService();

        Assert.False(await Persistencia.EjecutarAsync(
            () => throw new InvalidOperationException("Solo se puede cerrar un certificado en borrador."), notif));

        var aviso = Assert.Single(notif.Messages);
        Assert.Equal("No se pudo completar", aviso.Summary);
        Assert.Equal("Solo se puede cerrar un certificado en borrador.", aviso.Detail);
    }

    [Fact]
    public async Task Persistencia_Conflicto_UsaElMensajeDeLaPagina()
    {
        // Las páginas que conservan lo tipeado tras un conflicto no pueden pedir «Recargá la
        // página»: recargar borra justamente lo que se conservó.
        var conflicto = new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("x");

        var generico = new Radzen.NotificationService();
        Assert.False(await Persistencia.EjecutarAsync(() => throw conflicto, generico));
        Assert.Contains("Recargá la página", Assert.Single(generico.Messages).Detail);

        var propio = new Radzen.NotificationService();
        Assert.False(await Persistencia.EjecutarAsync(() => throw conflicto, propio, conflicto: "Se conservó lo que tipeaste."));
        var aviso = Assert.Single(propio.Messages);
        Assert.Equal("Conflicto de concurrencia", aviso.Summary);
        Assert.Equal("Se conservó lo que tipeaste.", aviso.Detail);
    }

    // ── Temas (cookie SIGOTheme) ────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("humanistic")]
    [InlineData("material-dark\"; x=1")]
    public void Temas_SinCookieOAdulteradaArrancaEnClaro(string? cookie)
    {
        // El oscuro como respaldo hacía que todo usuario sin cookie arrancara en oscuro.
        Assert.Equal(Temas.Claro, Temas.Normalizar(cookie));
    }

    [Theory]
    [InlineData("material", "material")]
    [InlineData("MATERIAL-DARK", "material-dark")]
    [InlineData("material-dark", "material-dark")]
    public void Temas_RespetaLaEleccionGuardada(string cookie, string esperado)
    {
        Assert.Equal(esperado, Temas.Normalizar(cookie));
    }

    // ── RetornoObra (a dónde vuelven las secciones de una obra) ─────────────────

    [Fact]
    public void RetornoObra_SinMarcaVuelveALaFicha()
    {
        Assert.Equal(("obras/5", "Volver a Obra"), new RetornoObra().Volver(5));
    }

    [Fact]
    public void RetornoObra_DesdeElListadoVuelveAObras_SoloEsaObra()
    {
        var retorno = new RetornoObra();
        retorno.DesdeListado(5);

        Assert.Equal(("obras", "Volver a Obras"), retorno.Volver(5));
        // La marca es de una obra: otra que se abra sin pasar por el listado vuelve a su ficha.
        Assert.Equal(("obras/7", "Volver a Obra"), retorno.Volver(7));
    }

    [Fact]
    public void RetornoObra_PasarPorLaFichaVuelveALaFicha()
    {
        var retorno = new RetornoObra();
        retorno.DesdeListado(5);
        retorno.DesdeFicha();

        Assert.Equal(("obras/5", "Volver a Obra"), retorno.Volver(5));
    }

    // ── Instantanea (cambios sin guardar de un formulario) ──────────────────────

    private sealed class Formulario
    {
        public string Nombre { get; set; } = "";
        public DateTime? Fecha { get; set; }
    }

    [Fact]
    public void Instantanea_DetectaCambiosYVolverAlValorOriginalNoEsCambio()
    {
        var modelo = new Formulario { Nombre = "Obra" };
        var foto = new Instantanea<Formulario>();
        Assert.False(foto.Cambio(modelo)); // sin foto todavía: no hay cambios

        foto.Tomar(modelo);
        Assert.False(foto.Cambio(modelo));

        modelo.Fecha = new DateTime(2026, 10, 1);
        Assert.True(foto.Cambio(modelo));

        modelo.Fecha = null;
        Assert.False(foto.Cambio(modelo));
        Assert.False(foto.Cambio(null));
    }

    [Fact]
    public void Instantanea_ObraVMSeSerializaEntero()
    {
        // El formulario de obra la evalúa en cada render: si el VM (con sus propiedades
        // calculadas, Estado y Presupuestos) no se pudiera serializar, la página caería.
        var obra = new SIGO.Models.ViewModels.ObraVM { Nombre = "Línea F", FechaActaInicio = new DateTime(2026, 3, 1) };
        var foto = new Instantanea<SIGO.Models.ViewModels.ObraVM>();
        foto.Tomar(obra);
        Assert.False(foto.Cambio(obra));

        obra.Contratista = "Constructora";
        Assert.True(foto.Cambio(obra));
    }

    [Fact]
    public void MesNombreSeguro_AcotaLosDatosSucios()
    {
        Assert.Equal("Marzo", Fmt.MesNombreSeguro(3));
        Assert.Equal("Mes 13", Fmt.MesNombreSeguro(13));
        Assert.Equal("Mes 0", Fmt.MesNombreSeguro(0));
    }

    // ── Orden cronológico de las publicaciones INDEC ─────────────────────────────

    [Fact]
    public void PublicacionIndec_TryPeriodo_LeeElSufijoMesAnio()
    {
        Assert.True(PublicacionIndec.TryPeriodo("INDEC_INFORMA_04_26", out var anio, out var mes));
        Assert.Equal(2026, anio);
        Assert.Equal(4, mes);
    }

    [Fact]
    public void PublicacionIndec_TryPeriodo_RechazaLoQueNoSigueElPatron()
    {
        Assert.False(PublicacionIndec.TryPeriodo(null, out _, out _));
        Assert.False(PublicacionIndec.TryPeriodo("TASAS_BNA", out _, out _));
        Assert.False(PublicacionIndec.TryPeriodo("INDEC_INFORMA_13_26", out _, out _)); // mes fuera de rango
    }

    [Fact]
    public void PublicacionIndec_Orden_EsCronologicoNoAlfabetico()
    {
        // El caso que rompía: alfabéticamente "…12_26" > "…01_27" (compara el MES primero),
        // así que la "última publicación" era la de diciembre del año viejo.
        string[] pubs = ["INDEC_INFORMA_12_26", "INDEC_INFORMA_01_27", "INDEC_INFORMA_04_26"];

        var ordenadas = pubs.OrderBy(PublicacionIndec.Orden).ToList();

        Assert.Equal(["INDEC_INFORMA_04_26", "INDEC_INFORMA_12_26", "INDEC_INFORMA_01_27"], ordenadas);
        Assert.Equal("INDEC_INFORMA_01_27", ordenadas.Last()); // la que preselecciona Calcular
    }

    [Fact]
    public void PublicacionIndec_Orden_LasNoFechablesNuncaQuedanUltimas()
    {
        string[] pubs = ["ZZZ_SIN_PATRON", "INDEC_INFORMA_01_26"];
        Assert.Equal("INDEC_INFORMA_01_26", pubs.OrderBy(PublicacionIndec.Orden).Last());
    }

    // ── Árbol de estructura ──────────────────────────────────────────────────────

    private sealed record Nodo(int Id, int? Padre, bool EsAgrupador, decimal Monto = 0);

    private static Dictionary<int, decimal> Sumas(params Nodo[] nodos) =>
        Arbol.SumaPorAgrupador(nodos, n => n.Padre, n => n.Id, n => n.EsAgrupador, n => n.Monto);

    [Fact]
    public void SumaPorAgrupador_UnRubroSumaTambienLoDeSusSubRubros()
    {
        // Rubro 1 › (hoja 2 de $10, Sub-rubro 3 › (hojas 4 y 5)); Rubro 6 sin hojas.
        var sumas = Sumas(
            new(1, null, true), new(2, 1, false, 10m), new(3, 1, true),
            new(4, 3, false, 100m), new(5, 3, false, 1000m), new(6, null, true));

        Assert.Equal(1110m, sumas[1]);   // antes solo la hoja directa: $10
        Assert.Equal(1100m, sumas[3]);
        Assert.False(sumas.ContainsKey(6));
    }

    [Fact]
    public void SumaPorAgrupador_UnCicloEnLosDatosNoCuelga()
    {
        var sumas = Sumas(new(1, 2, true), new(2, 1, true), new(3, 1, false, 5m));

        Assert.Equal(5m, sumas[1]);
        Assert.Equal(5m, sumas[2]);
    }

    [Fact]
    public void PubMayus_SigueEtiquetandoIgualSobreElParseoCompartido()
    {
        Assert.Equal("ABRIL/2026", Fmt.PubMayus("INDEC_INFORMA_04_26"));
        Assert.Equal("—", Fmt.PubMayus(null));
        Assert.Equal("TASAS BNA", Fmt.PubMayus("TASAS_BNA"));
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(12.5, true)]
    [InlineData(3.9999, false)]
    [InlineData(-6, false)]   // una baja no habilita: Calcular comparaba el valor absoluto
    public void Redet_SoloUnaSubaDeAlMenosElSaltoMinimoHabilita(double porcentaje, bool habilita) =>
        Assert.Equal(habilita, Redet.EsSuba((decimal)porcentaje));
}
