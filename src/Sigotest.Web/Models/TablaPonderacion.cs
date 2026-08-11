namespace SIGO.Models;

public class TablaPonderacion : BaseEntity
{
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;

    public string Nombre { get; set; } = "Tabla Principal";

    public ICollection<ItemPonderacion> Items { get; set; } = [];
}
