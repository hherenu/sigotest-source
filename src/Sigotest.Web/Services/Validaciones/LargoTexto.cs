namespace SIGO.Services.Validaciones;

/// <summary>
/// Largos máximos de las columnas de texto. AppDbContext los configura en el modelo y los
/// formularios los usan como MaxLength, para que no se pueda tipear más de lo que entra en
/// la base: antes un texto largo terminaba en un error de SQL en inglés. Lo que llega por
/// otro camino (importaciones) lo frena AppDbContext antes de guardar, con un mensaje.
/// </summary>
public static class LargoTexto
{
    /// <summary>Convención de todas las columnas de texto sin un largo propio.</summary>
    public const int PorDefecto = 250;

    /// <summary>Descripciones (ítem de estructura, descripción INDEC) y correos del director.</summary>
    public const int Descripcion = 500;

    /// <summary>Textos libres: observaciones, motivo y correcciones de una revisión.</summary>
    public const int Observaciones = 2000;

    public const int CodigoIndice = 50;
}
