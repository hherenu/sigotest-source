namespace SIGO.Services.Validaciones;

/// <summary>
/// Puente entre los validadores puros y los servicios: cada regla devuelve el mensaje
/// de error (o null si pasa) para que la UI pueda mostrarlo sin try/catch; el servicio
/// lo convierte en excepción con este helper.
/// </summary>
public static class Validacion
{
    public static void Exigir(string? error)
    {
        if (error is not null) throw new InvalidOperationException(error);
    }
}
