using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SIGO.Services;

namespace SIGO.Tests;

/// <summary>
/// Contrato del valor de retorno de EnviarAsync (destinatarios efectivamente
/// incluidos): el digest registra el mes según este valor, así que 0 tiene que
/// significar "no salió nada" — en particular con direcciones legadas inválidas,
/// que se descartan por unidad ANTES de conectar al SMTP (por eso estos tests
/// no necesitan red: el corte ocurre con el To vacío).
/// </summary>
public class SmtpEmailSenderTests
{
    private sealed class OptionsMonitorStub<T>(T valor) : IOptionsMonitor<T>
    {
        public T CurrentValue => valor;
        public T Get(string? name) => valor;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static SmtpEmailSender Crear(bool habilitado) => new(
        new OptionsMonitorStub<CorreoOptions>(new CorreoOptions
        {
            Habilitado = habilitado,
            SmtpHost = "smtp.test.local",
            Remitente = "sigo@test.local"
        }),
        NullLogger<SmtpEmailSender>.Instance);

    [Fact]
    public async Task Direccion_invalida_unica_devuelve_cero()
    {
        // El caso del digest: CorreoDirectorObra legado sin @. Se descarta, no queda
        // ningún destinatario y el resultado debe ser 0 (el digest NO cuenta el envío
        // y el mes queda libre para reintentar cuando se corrija el dato).
        var resultado = await Crear(habilitado: true)
            .EnviarAsync(["juan.perez"], "Asunto", "<p>hola</p>");

        Assert.Equal(0, resultado);
    }

    [Fact]
    public async Task Solo_blancos_devuelve_cero()
    {
        var resultado = await Crear(habilitado: true)
            .EnviarAsync(["", "   "], "Asunto", "<p>hola</p>");

        Assert.Equal(0, resultado);
    }

    [Fact]
    public async Task Correo_deshabilitado_cuenta_los_destinatarios()
    {
        // Con el correo apagado los mails solo se loguean, pero se informan como
        // incluidos: los llamadores no deben quedar reintentando por siempre
        // (el digest registra el mes igual — semántica vigente).
        var resultado = await Crear(habilitado: false)
            .EnviarAsync(["a@test.local", "b@test.local"], "Asunto", "<p>hola</p>");

        Assert.Equal(2, resultado);
    }
}
