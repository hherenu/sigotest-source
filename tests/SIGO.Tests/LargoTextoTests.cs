using Microsoft.EntityFrameworkCore;
using SIGO.Models;
using SIGO.Services.Validaciones;

namespace SIGO.Tests;

/// <summary>
/// Textos más largos que su columna: AppDbContext los frena antes de guardar con un mensaje
/// que muestra el comienzo del texto. Antes llegaban a la base y volvía un error de SQL en
/// inglés («String or binary data would be truncated»).
/// </summary>
public class LargoTextoTests(LocalDbFixture fx) : IClassFixture<LocalDbFixture>
{
    [Fact]
    public async Task TextoMasLargoQueSuColumna_SeRechazaConElMotivo_SinGuardar()
    {
        var nombre = "Obra " + new string('x', LargoTexto.PorDefecto);

        await using (var db = fx.CrearContexto())
        {
            db.Obras.Add(new Obra { Nombre = nombre, NumeroLicitacion = "LP LARGO" });
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Equal($"Un texto tiene {nombre.Length} caracteres y el máximo es {LargoTexto.PorDefecto}: " +
                         $"«{nombre[..40]}…». Acortalo y volvé a guardar.", ex.Message);
        }

        await using (var db = fx.CrearContexto())
            Assert.False(await db.Obras.AnyAsync(o => o.NumeroLicitacion == "LP LARGO"));
    }

    [Fact]
    public async Task TextoDelLargoDeSuColumna_SeGuarda()
    {
        await using var db = fx.CrearContexto();
        db.Obras.Add(new Obra { Nombre = new string('x', LargoTexto.PorDefecto), NumeroLicitacion = "LP JUSTO" });

        await db.SaveChangesAsync();
    }
}
