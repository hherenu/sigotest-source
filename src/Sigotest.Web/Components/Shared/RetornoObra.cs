namespace SIGO.Components.Shared;

/// <summary>
/// A dónde vuelven las secciones de una obra (estructuras, certificados, ponderación): a la
/// ficha de la obra o al listado de Obras, según por dónde se entró. Las columnas de acceso del
/// listado lo marcan y la ficha lo desmarca; las pantallas internas (ítems, carga o detalle de un
/// certificado…) no lo tocan, así que al volver a la sección sigue valiendo. Scoped, como
/// <see cref="EstadoGrillas"/>: vive lo que vive el circuito (la pestaña) y un F5 vuelve a la ficha.
/// </summary>
public sealed class RetornoObra
{
    private int? obraDesdeListado;

    /// <summary>Se entra a una sección de esa obra desde el listado de Obras.</summary>
    public void DesdeListado(int obraId) => obraDesdeListado = obraId;

    /// <summary>Se pasa por la ficha de una obra: desde ahí, sus secciones vuelven a ella.</summary>
    public void DesdeFicha() => obraDesdeListado = null;

    /// <summary>Destino y texto del botón «Volver» de una sección de esa obra.</summary>
    public (string Ruta, string Texto) Volver(int obraId) =>
        obraDesdeListado == obraId ? ("obras", "Volver a Obras") : ($"obras/{obraId}", "Volver a Obra");
}
