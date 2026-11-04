using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace SIGO.Services;

/// <summary>
/// Reglas de la sección "Correo", evaluadas al arrancar (ValidateOnStart): un valor
/// imposible frena el arranque con el motivo en el log. Sin esto, el digest o el rollover
/// dejaban de correr algunos meses, o todos los mails fallaban, sin que nadie se enterara.
/// </summary>
public sealed class ValidarCorreoOptions : IValidateOptions<CorreoOptions>
{
    /// <summary>Los días 29 a 31 no existen en todos los meses: la tarea no correría en esos meses.</summary>
    public const int UltimoDiaDelMes = 28;

    public ValidateOptionsResult Validate(string? name, CorreoOptions o)
    {
        var errores = new List<string>();

        if (o.DiaDigest is < 1 or > UltimoDiaDelMes)
            errores.Add($"Correo:DiaDigest debe estar entre 1 y {UltimoDiaDelMes} (es {o.DiaDigest}): " +
                "con un día que el mes no tiene, ese mes no sale el digest.");
        if (o.DiaRollover is < 1 or > UltimoDiaDelMes)
            errores.Add($"Correo:DiaRollover debe estar entre 1 y {UltimoDiaDelMes} (es {o.DiaRollover}): " +
                "con un día que el mes no tiene, ese mes no se reinicia el ciclo ni sale el digest.");
        if (o.TimeoutSegundos < 1)
            errores.Add($"Correo:TimeoutSegundos debe ser mayor que 0 (es {o.TimeoutSegundos}): " +
                "con 0 todos los envíos se cortan antes de empezar.");

        // Con el correo apagado los mails solo se loguean: el servidor y el remitente no se usan.
        if (o.Habilitado)
        {
            if (string.IsNullOrWhiteSpace(o.SmtpHost))
                errores.Add("Correo:SmtpHost es obligatorio con Correo:Habilitado en true: sin servidor no sale ningún mail.");
            if (o.SmtpPort is < 1 or > 65535)
                errores.Add($"Correo:SmtpPort debe estar entre 1 y 65535 (es {o.SmtpPort}).");
            if (!MailAddress.TryCreate(o.Remitente, out _))
                errores.Add($"Correo:Remitente no es una dirección de correo válida («{o.Remitente}»): fallarían todos los envíos.");
        }

        return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores);
    }
}
