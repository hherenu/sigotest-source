using SIGO.Services;

namespace SIGO.Tests;

/// <summary>Reglas de la sección "Correo" que frenan el arranque (ValidateOnStart).</summary>
public class ValidarCorreoOptionsTests
{
    private static Microsoft.Extensions.Options.ValidateOptionsResult Validar(CorreoOptions o) =>
        new ValidarCorreoOptions().Validate(null, o);

    [Fact]
    public void ValoresPorDefecto_SonValidos()
    {
        Assert.True(Validar(new CorreoOptions()).Succeeded);
        Assert.True(Validar(new CorreoOptions { DiaDigest = 1, DiaRollover = 28 }).Succeeded);
    }

    [Theory]
    [InlineData(0, 9, 10, "Correo:DiaDigest")]
    [InlineData(29, 9, 10, "Correo:DiaDigest")]     // febrero no tiene 29 todos los años
    [InlineData(31, 9, 10, "Correo:DiaDigest")]
    [InlineData(10, 0, 10, "Correo:DiaRollover")]
    [InlineData(10, 30, 10, "Correo:DiaRollover")]
    [InlineData(10, 9, 0, "Correo:TimeoutSegundos")] // con 0 todos los envíos se cortan
    public void ValoresImposibles_FrenanElArranqueConLaClave(int diaDigest, int diaRollover, int timeout, string clave)
    {
        var r = Validar(new CorreoOptions { DiaDigest = diaDigest, DiaRollover = diaRollover, TimeoutSegundos = timeout });

        Assert.True(r.Failed);
        Assert.StartsWith(clave, Assert.Single(r.Failures!));
    }

    [Fact]
    public void Habilitado_ExigeServidorPuertoYRemitenteValido()
    {
        var r = Validar(new CorreoOptions { Habilitado = true, SmtpHost = " ", SmtpPort = 0, Remitente = "sigo" });

        Assert.True(r.Failed);
        Assert.Equal(3, r.Failures!.Count());
        Assert.Contains(r.Failures!, f => f.StartsWith("Correo:SmtpHost", StringComparison.Ordinal));
        Assert.Contains(r.Failures!, f => f.StartsWith("Correo:SmtpPort", StringComparison.Ordinal));
        Assert.Contains(r.Failures!, f => f.StartsWith("Correo:Remitente", StringComparison.Ordinal));

        Assert.True(Validar(new CorreoOptions
        {
            Habilitado = true, SmtpHost = "relay.local", SmtpPort = 25, Remitente = "sigo@empresa.test"
        }).Succeeded);
    }

    [Fact]
    public void Deshabilitado_NoMiraServidorNiRemitente()
    {
        // Con el correo apagado los mails solo se loguean: appsettings.json viene así.
        Assert.True(Validar(new CorreoOptions { Habilitado = false, SmtpHost = "", Remitente = "" }).Succeeded);
    }
}
