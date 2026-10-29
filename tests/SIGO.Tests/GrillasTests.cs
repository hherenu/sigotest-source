using Radzen;
using Radzen.Blazor;
using SIGO.Components.Shared;
using SIGO.Models.Enums;
using SIGO.Models.ViewModels;

namespace SIGO.Tests;

/// <summary>
/// Piezas de los listados portadas de SAF: búsqueda rápida, estado que sobrevive a la
/// navegación y textos. Lo que hace la grilla con ese estado (reponer filtros, orden y
/// página) es de Radzen y se prueba a mano.
/// </summary>
public class GrillasTests
{
    private sealed record Fila(string Codigo, string? Nombre);

    private static readonly List<Fila> Datos =
    [
        new("MO", "Mano de obra"),
        new("ALB", "Albañilería"),
        new("IDX", "Índice general"),
        new("SN", null),
    ];

    private static VistaGrilla<Fila> Vista(EstadoGrilla estado)
    {
        var vista = new VistaGrilla<Fila>(estado, f => [f.Codigo, f.Nombre]);
        vista.Cargar(Datos);
        return vista;
    }

    private static List<string> Codigos(VistaGrilla<Fila> vista) => vista.Filas.Select(f => f.Codigo).ToList();

    [Fact]
    public void Buscar_SinDistinguirMayusculasNiTildes()
    {
        var vista = Vista(new EstadoGrilla());

        vista.Buscar("  indice ");
        Assert.Equal(["IDX"], Codigos(vista));

        vista.Buscar("ALBANI");
        Assert.Equal(["ALB"], Codigos(vista));
    }

    [Fact]
    public void Buscar_RecorreTodosLosCamposYToleraLosVacios()
    {
        var vista = Vista(new EstadoGrilla());

        vista.Buscar("sn");
        Assert.Equal(["SN"], Codigos(vista));

        vista.Buscar("obra");
        Assert.Equal(["MO"], Codigos(vista));
        Assert.Equal(1, vista.Visibles);
        Assert.Equal(4, vista.Total);
    }

    [Fact]
    public void SinBusqueda_MuestraTodas_YLaDataNoCambiaEntreRenders()
    {
        var vista = Vista(new EstadoGrilla());
        vista.Buscar("mano");
        vista.Buscar("");

        Assert.Equal(4, vista.Filas.Count);
        // Radzen vuelve a la página 1 con cada Data nueva: leerla dos veces es la misma lista.
        Assert.Same(vista.Filas, vista.Filas);
    }

    [Fact]
    public void LaBusquedaSobreviveAlVolverAlListado_ySoloEnEseListado()
    {
        var estados = new EstadoGrillas();
        Vista(estados.De("obras")).Buscar("mano");

        // Volver al listado crea otra página (y otra vista) sobre el mismo servicio.
        var alVolver = Vista(estados.De("obras"));
        Assert.Equal("mano", alVolver.Busqueda);
        Assert.Equal(["MO"], Codigos(alVolver));

        var otro = Vista(estados.De("indices"));
        Assert.Equal("", otro.Busqueda);
        Assert.Equal(4, otro.Filas.Count);
    }

    [Fact]
    public void Settings_SeGuardanYSeReponenEnLaGrillaNueva()
    {
        var estado = new EstadoGrilla();
        var sinGuardar = new DataGridLoadSettingsEventArgs();
        Vista(estado).CargarSettings(sinGuardar);
        Assert.Null(sinGuardar.Settings);

        var settings = new DataGridSettings { CurrentPage = 2 };
        Vista(estado).GuardarSettings(settings);

        var args = new DataGridLoadSettingsEventArgs();
        Vista(estado).CargarSettings(args);
        Assert.Same(settings, args.Settings);
    }

    [Fact]
    public void HayFiltros_PorBusquedaOPorFiltroGuardado()
    {
        var estado = new EstadoGrilla();
        var vista = Vista(estado);
        Assert.False(vista.HayFiltros);

        vista.Buscar("mano");
        Assert.True(vista.HayFiltros);
        vista.Buscar(" ");
        Assert.False(vista.HayFiltros);

        // Al volver al listado la cabecera se pinta antes de que la grilla reponga lo guardado.
        var columna = new DataGridColumnSettings { Property = nameof(Fila.Nombre), FilterValue = new List<object>() };
        estado.Settings = new DataGridSettings { Columns = [columna] };
        Assert.False(vista.HayFiltros); // lista de tildes vacía: no filtra

        columna.FilterValue = new List<object> { "Mano de obra" };
        Assert.True(vista.HayFiltros);

        columna.FilterValue = null;
        columna.SecondFilterValue = "obra";
        Assert.True(vista.HayFiltros);
    }

    [Fact]
    public async Task QuitarFiltros_LimpiaBusquedaYFiltros_SinTocarAnchosNiOrden()
    {
        var columna = new DataGridColumnSettings
        {
            Property = nameof(Fila.Nombre),
            Width = "200px",
            SortOrder = SortOrder.Descending,
            FilterValue = new List<object> { "Mano de obra" },
            SecondFilterValue = "obra",
        };
        var estado = new EstadoGrilla { Settings = new DataGridSettings { Columns = [columna] } };
        var vista = Vista(estado);
        vista.Buscar("mano");

        await vista.QuitarFiltrosAsync();

        Assert.False(vista.HayFiltros);
        Assert.Equal("", vista.Busqueda);
        Assert.Equal(4, vista.Filas.Count);
        Assert.Null(columna.FilterValue);
        Assert.Null(columna.SecondFilterValue);
        Assert.Equal("200px", columna.Width);
        Assert.Equal(SortOrder.Descending, columna.SortOrder);
    }

    [Fact]
    public void ConLaGrillaViva_OtraDataLlevaLaPaginaGuardadaALaPrimera()
    {
        // Radzen vuelve a la página 1 con la Data nueva sin avisar a SettingsChanged: sin
        // esto, al volver se repondría una página que con menos filas puede no existir.
        var estado = new EstadoGrilla { Settings = new DataGridSettings { CurrentPage = 3 } };
        var vista = Vista(estado);
        vista.Grid = new RadzenDataGrid<Fila>();

        vista.Buscar("mano");

        Assert.Equal(0, estado.Settings.CurrentPage);
    }

    [Fact]
    public void AlVolverAlListado_LaCargaConservaLaPaginaGuardada()
    {
        // Sin grilla todavía (la página recién creada): la página guardada es la que hay que reponer.
        var estado = new EstadoGrilla { Settings = new DataGridSettings { CurrentPage = 3 } };

        Vista(estado);

        Assert.Equal(3, estado.Settings.CurrentPage);
    }

    [Fact]
    public void AlReponer_UnaPaginaQueYaNoExisteVuelveALaUltima()
    {
        // Se editó o se borró la fila que llenaba la página 2: reponerla dejaba la grilla
        // vacía con el pie diciendo que había filas.
        var estado = new EstadoGrilla { Settings = new DataGridSettings { CurrentPage = 1, PageSize = 20 } };
        var args = new DataGridLoadSettingsEventArgs();
        Vista(estado).CargarSettings(args); // 4 filas: solo existe la página 0

        Assert.Same(estado.Settings, args.Settings);
        Assert.Equal(0, estado.Settings.CurrentPage);

        var muchas = new VistaGrilla<Fila>(estado, f => [f.Codigo]);
        muchas.Cargar(Enumerable.Range(1, 45).Select(i => new Fila($"F{i}", null)).ToList());
        estado.Settings.CurrentPage = 2;
        muchas.CargarSettings(new DataGridLoadSettingsEventArgs());
        Assert.Equal(2, estado.Settings.CurrentPage); // filas 41 a 45: existe
    }

    [Fact]
    public void AlReponer_SeQuitanLosFiltrosDeColumnasQueLaGrillaYaNoTiene()
    {
        // Planificación: «Versión al corte» solo existe con un corte elegido. Su filtro guardado
        // mostraba «Quitar filtros» sin que nada estuviera filtrado.
        var presente = new DataGridColumnSettings { Property = nameof(Fila.Nombre), FilterValue = new List<object> { "Mano de obra" } };
        var ausente = new DataGridColumnSettings { Property = "VersionAlCorte", FilterValue = new List<object> { "Sin versión" } };
        var estado = new EstadoGrilla { Settings = new DataGridSettings { Columns = [presente, ausente] } };
        var vista = Vista(estado);
        var avisos = 0;
        vista.Cambio += () => avisos++;

        // Sin grilla (o sin columnas registradas) no se sabe cuáles faltan: no se toca nada.
        vista.CargarSettings(new DataGridLoadSettingsEventArgs());
        Assert.NotNull(ausente.FilterValue);

        var grilla = new RadzenDataGrid<Fila>();
#pragma warning disable BL0005 // columna armada a mano: no hay renderer en el test
        grilla.ColumnsCollection.Add(new RadzenDataGridColumn<Fila> { Property = nameof(Fila.Nombre) });
#pragma warning restore BL0005
        vista.Grid = grilla;
        vista.CargarSettings(new DataGridLoadSettingsEventArgs());

        Assert.NotNull(presente.FilterValue);
        Assert.Null(ausente.FilterValue);
        Assert.Equal(1, avisos);

        vista.CargarSettings(new DataGridLoadSettingsEventArgs());
        Assert.Equal(1, avisos); // sin nada que quitar no avisa: no hay bucle de redibujos
    }

    [Fact]
    public void RadzenSigueTeniendoComoRefrescarLasOpcionesDelFiltro()
    {
        // VistaGrilla vacía por reflexión la caché de opciones de cada columna (método interno
        // de Radzen 10.4.3). Si una actualización lo renombra, las opciones quedarían viejas.
        Assert.True(VistaGrilla<Fila>.PuedeRefrescarOpciones);
    }

    [Fact]
    public void Vacio_SinFilasNoNombraUnBotonQueNoEsta()
    {
        Assert.Equal(GridTextos.SinRegistros, GridTextos.VacioListado(0));
        Assert.Equal(GridTextos.SinCoincidenciasListado, GridTextos.VacioListado(3));
    }

    [Fact]
    public async Task Cambio_AvisaALaGrillaYALaBusqueda()
    {
        // La grilla (GrillaListado) y la búsqueda (FiltrosGrilla) son componentes distintos:
        // se redibujan con este aviso, no con el de la página.
        var vista = Vista(new EstadoGrilla());
        var avisos = 0;
        vista.Cambio += () => avisos++;

        vista.Buscar("mano");
        vista.GuardarSettings(new DataGridSettings());
        vista.Cargar(Datos);
        await vista.QuitarFiltrosAsync();

        Assert.Equal(4, avisos);
    }

    [Fact]
    public void ColumnasCalculadas_ElTextoQueSeVeEsElQueFiltra()
    {
        var cert = new CertificadoListItemVM { Mes = 9, Anio = 2026 };
        Assert.Equal("Septiembre 2026", cert.Periodo);
        // El texto ordenaría «Agosto» antes que «Enero»: el orden es el cronológico.
        Assert.True(new CertificadoListItemVM { Mes = 1, Anio = 2027 }.PeriodoOrden > cert.PeriodoOrden);
        Assert.Equal("Enero 2026", new ValorIndiceVM { Mes = 1, Anio = 2026 }.Periodo);

        var usuario = new UsuarioVM { Roles = [RolUsuario.Gerente, RolUsuario.Admin], Activo = false };
        Assert.Equal("Admin, Gerente", usuario.RolesTexto);
        Assert.Equal("Inactivo", usuario.ActivoTexto);
        Assert.Equal("Activo", new UsuarioVM().ActivoTexto);
        // Sin roles el filtro mostraba una casilla sin texto.
        Assert.Equal(UsuarioVM.SinRoles, new UsuarioVM { Roles = [] }.RolesTexto);
    }

    [Fact]
    public void Planificacion_VersionAlCorte()
    {
        Assert.Equal("Sin versión", PlanObraRowVM.TextoVersionAlCorte(null));
        var tomada = new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc);
        Assert.Equal($"Sep 2026 — conocida el {tomada.ToLocalTime():dd/MM/yyyy}",
            PlanObraRowVM.TextoVersionAlCorte(new VersionCorteVM(2026, 9, tomada)));
    }

    [Fact]
    public void Planificacion_EstadoYCicloDelMes()
    {
        Assert.Equal("Sin iniciar", new PlanObraRowVM().EstadoPlanTexto);
        Assert.Equal(nameof(EstadoPlanificacion.Aprobada), new PlanObraRowVM { EstadoPlan = EstadoPlanificacion.Aprobada }.EstadoPlanTexto);
        // En texto plano (sin badge) se leía «EnRevision», el nombre del enum.
        Assert.Equal("En revisión", new PlanObraRowVM { EstadoPlan = EstadoPlanificacion.EnRevision }.EstadoPlanTexto);

        Assert.Equal(PlanObraRowVM.SinCiclo, new PlanObraRowVM { EstadoPlan = EstadoPlanificacion.Pendiente }.CicloDelMes);
        Assert.Equal("Falta toma de conocimiento", new PlanObraRowVM { EstadoPlan = EstadoPlanificacion.Aprobada }.CicloDelMes);
        var tomada = new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc);
        Assert.Equal($"Conocida el {tomada.ToLocalTime():dd/MM}",
            new PlanObraRowVM { EstadoPlan = EstadoPlanificacion.Aprobada, FechaTomaConocimiento = tomada }.CicloDelMes);
        // Finalizada gana aunque tenga toma de conocimiento.
        Assert.Equal("Obra finalizada",
            new PlanObraRowVM { ObraFinalizada = true, FechaTomaConocimiento = tomada }.CicloDelMes);
    }

    [Theory]
    [InlineData(20, 20, "20 filas")]
    [InlineData(1, 1, "1 fila")]
    [InlineData(0, 0, "0 filas")]
    [InlineData(3, 20, "3 de 20 filas (vista filtrada)")]
    [InlineData(120, 1450, "120 de 1.450 filas (vista filtrada)")]
    public void Pie_DiceDeCuantasSoloSiHayFiltro(int visibles, int total, string esperado)
    {
        Assert.Equal(esperado, GridTextos.Filas(visibles, total));
    }
}
