using SIGO.Services.Validaciones;

namespace SIGO.Tests;

/// <summary>
/// ID de publicación de la importación de publicaciones: obligatorio y con formato fechable.
/// Un import sin id dejaba la publicación invisible en la pantalla de calcular (que filtra
/// IdPublicacion != null), y uno no fechable nunca ganaba como "más reciente".
/// </summary>
public class IndiceValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PublicacionImportacion_Vacia_EsObligatoria(string? id)
    {
        Assert.Contains("obligatorio", IndiceValidator.PublicacionImportacion(id));
    }

    [Theory]
    [InlineData("REVISTA_AGOSTO")]      // nomenclatura libre: no fechable
    [InlineData("08_26")]               // sufijo fechable pero sin el prefijo canónico
    [InlineData("INDEC_INFORMA_13_26")] // mes fuera de rango
    [InlineData("INDEC_INFORMA_08")]    // sufijo incompleto
    [InlineData("INDEC_INFORMA_8_26")]  // mes de un dígito: sería otra publicación «distinta» de la 08_26
    [InlineData("INDEC_INFORMA_08_2026")] // año de cuatro dígitos: ídem
    [InlineData("indec_informa_08_26")] // minúsculas: el duplicado de publicación se detecta por texto exacto
    [InlineData("INDEC_INFORMA_00_26")] // mes cero
    [InlineData("INDEC_INFORMA_0 _26")] // espacio interior
    [InlineData("INDEC_INFORMA_0A_26")] // carácter no numérico
    [InlineData("INDEC_INFORMA_08_26X")] // un carácter de más
    public void PublicacionImportacion_FormatoInvalido_Rechaza(string id)
    {
        Assert.Contains("INDEC_INFORMA_MM_AA", IndiceValidator.PublicacionImportacion(id));
    }

    [Theory]
    [InlineData("INDEC_INFORMA_08_26")]
    [InlineData("  INDEC_INFORMA_12_25  ")] // NormalizarPublicacion trimea antes de guardar
    public void PublicacionImportacion_FormatoValido_Pasa(string id)
    {
        Assert.Null(IndiceValidator.PublicacionImportacion(id));
    }
}
