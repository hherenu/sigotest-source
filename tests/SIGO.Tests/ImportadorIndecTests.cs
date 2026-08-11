using SIGO.Services;

namespace SIGO.Tests;

/// <summary>
/// Parser del pegado de publicaciones INDEC. La regla más delicada es la de
/// decimales: interpretar "8834.3" como es-AR daría 88343 (×10.000 en un índice).
/// </summary>
public class ImportadorIndecTests
{
    [Fact]
    public void DosColumnas_UsaElPeriodoPorDefecto()
    {
        var filas = ImportadorIndec.ParsearLineas("ALBANILERIA\t11883,8", 2026, 3);

        var f = Assert.Single(filas);
        Assert.Equal(EstadoFilaImport.Ok, f.Estado);
        Assert.Equal("ALBANILERIA", f.Codigo);
        Assert.Equal((2026, 3, 11883.8m), (f.Anio, f.Mes, f.Valor));
    }

    [Fact]
    public void CuatroColumnas_TraeSuPropioPeriodo()
    {
        var filas = ImportadorIndec.ParsearLineas("MANO_OBRA;2025;12;10251,2", 2026, 3);

        var f = Assert.Single(filas);
        Assert.Equal((2025, 12, 10251.2m), (f.Anio, f.Mes, f.Valor));
    }

    [Theory]
    [InlineData("10.819,3", 10819.3)]   // es-AR: punto de miles + coma decimal
    [InlineData("11883,8", 11883.8)]    // es-AR: coma decimal
    [InlineData("8834.3", 8834.3)]      // invariante: punto decimal (NO 88343)
    [InlineData("10819", 10819)]        // entero
    public void TryValor_InterpretaLaCulturaCorrecta(string texto, decimal esperado)
    {
        Assert.True(ImportadorIndec.TryValor(texto, out var v));
        Assert.Equal(esperado, v);
    }

    [Fact]
    public void FilasInvalidas_SeMarcanSinCortarElResto()
    {
        var texto = "ALBANILERIA\t100,5\n" +   // ok
                    "PINTURA\t1\t2\n" +        // 3 columnas → inválida
                    "MADERA\t0\n" +            // valor cero → inválida
                    "\n" +                     // vacía → ignorada
                    "PVC\t200,1";              // ok

        var filas = ImportadorIndec.ParsearLineas(texto, 2026, 1);

        Assert.Equal(4, filas.Count);
        Assert.Equal(EstadoFilaImport.Ok, filas[0].Estado);
        Assert.Equal(EstadoFilaImport.FilaInvalida, filas[1].Estado);
        Assert.Equal(EstadoFilaImport.FilaInvalida, filas[2].Estado);
        Assert.Equal(EstadoFilaImport.Ok, filas[3].Estado);
        Assert.Equal(5, filas[3].Linea); // el número de línea original se conserva
    }

    [Fact]
    public void MesOAnioFueraDeRango_EsFilaInvalida()
    {
        var filas = ImportadorIndec.ParsearLineas("PVC;2025;13;100\nPVC;1999;5;100", 2026, 1);

        Assert.All(filas, f => Assert.Equal(EstadoFilaImport.FilaInvalida, f.Estado));
    }

    [Fact]
    public void PegadoVacio_DevuelveListaVacia()
    {
        Assert.Empty(ImportadorIndec.ParsearLineas(null, 2026, 1));
        Assert.Empty(ImportadorIndec.ParsearLineas("   \n  ", 2026, 1));
    }
}
