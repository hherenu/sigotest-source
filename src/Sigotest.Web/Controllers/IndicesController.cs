using Microsoft.AspNetCore.Mvc;
using SIGO.Services;
using SIGO.Services.Importacion;
using SIGO.Services.Validaciones;

namespace SIGO.Controllers;

/// <summary>
/// La UI de índices vive en Blazor (Components/Pages/Indices). Este controller solo sirve la
/// descarga de la plantilla Excel (<see cref="PlantillaIndices"/>), que es una respuesta de archivo.
/// </summary>
[Microsoft.AspNetCore.Authorization.Authorize(Roles = Roles.Certificaciones)]
public class IndicesController(IndiceService service) : Controller
{
    // GET: /Indices/DescargarPlantilla?idPublicacion=INDEC_INFORMA_07_26
    // La publicación define los meses de la plantilla y viaja dentro del archivo: al
    // subirlo, la página la toma de ahí.
    [HttpGet]
    public async Task<IActionResult> DescargarPlantilla(string? idPublicacion)
    {
        if (IndiceValidator.PublicacionImportacion(idPublicacion) is string error)
            return BadRequest(error);

        var id = idPublicacion!.Trim();
        var archivo = PlantillaIndices.Crear(id, await service.ListarAsync());
        return File(archivo, ExcelHelpers.ContentType, $"Plantilla_indices_{id}.xlsx");
    }
}
