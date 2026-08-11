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

    [Fact]
    public void PubMayus_SigueEtiquetandoIgualSobreElParseoCompartido()
    {
        Assert.Equal("ABRIL/2026", Fmt.PubMayus("INDEC_INFORMA_04_26"));
        Assert.Equal("—", Fmt.PubMayus(null));
        Assert.Equal("TASAS BNA", Fmt.PubMayus("TASAS_BNA"));
    }
}
