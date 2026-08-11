using System.ComponentModel.DataAnnotations;

namespace SIGO.Models;

/// <summary>
/// Base de auditoría para los agregados raíz. Los campos se poblan automáticamente
/// en <c>AppDbContext.SaveChanges</c>. Preparada para Identity: los *UserId apuntan
/// al Id de AspNetUsers (nvarchar(450)) sin FK por ahora.
/// </summary>
public abstract class BaseEntity
{
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    /// <summary>Token de concurrencia (rowversion de SQL Server).</summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
