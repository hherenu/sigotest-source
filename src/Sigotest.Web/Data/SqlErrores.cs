using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace SIGO.Data;

/// <summary>Errores de SQL Server que un servicio espera y resuelve como parte de su flujo.</summary>
public static class SqlErrores
{
    /// <summary>
    /// Violación de un índice único o de una clave primaria (2601/2627). Atrapar cualquier
    /// DbUpdateException como si fuera esto tragaba también timeouts y deadlocks.
    /// </summary>
    public static bool EsClaveDuplicada(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
