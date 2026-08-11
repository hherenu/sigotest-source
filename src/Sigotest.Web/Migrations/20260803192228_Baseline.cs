using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SIGO.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DigestsPlanificacionEnviados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Anio = table.Column<int>(type: "int", nullable: false),
                    Mes = table.Column<int>(type: "int", nullable: false),
                    FechaEnvio = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CantidadMails = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigestsPlanificacionEnviados", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IndicesINDEC",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CodigoIndice = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FamiliaRecurso = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    IndiceNormalizado = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CuadroReferencia = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    IncisoCode = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndicesINDEC", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RolloversPlanificacionEjecutados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Anio = table.Column<int>(type: "int", nullable: false),
                    Mes = table.Column<int>(type: "int", nullable: false),
                    FechaEjecucion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CantidadPlanesReiniciados = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolloversPlanificacionEjecutados", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WindowsUser = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ValoresIndice",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IndiceId = table.Column<int>(type: "int", nullable: false),
                    Anio = table.Column<int>(type: "int", nullable: false),
                    Mes = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IdPublicacion = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValoresIndice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValoresIndice_IndicesINDEC_IndiceId",
                        column: x => x.IndiceId,
                        principalTable: "IndicesINDEC",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Obras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    NumeroLicitacion = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Antecedentes = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Contratista = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    PUByC = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    FechaOferta = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OfertaFinal = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    FechaAdjudicacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IFAdjudicacion = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    FechaContrato = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IFContrato = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    FechaActaInicio = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IFActaInicio = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    PlazoObra = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    FechaFinalContrato = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DirectorObra = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CorreoDirectorObra = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DirectorUsuarioId = table.Column<int>(type: "int", nullable: true),
                    IFDesignacion = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Obras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Obras_Usuarios_DirectorUsuarioId",
                        column: x => x.DirectorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UsuariosRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    Rol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuariosRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsuariosRoles_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Certificados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    Mes = table.Column<int>(type: "int", nullable: false),
                    Anio = table.Column<int>(type: "int", nullable: false),
                    FechaEmision = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FechaCierre = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Observaciones = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Certificados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Certificados_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EstructurasCostos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: true),
                    ActoAdministrativo = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Expediente = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    FechaAprobacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Observaciones = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstructurasCostos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EstructurasCostos_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Planificaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FechaCarga = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaAprobacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoRevision = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Correcciones = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FechaTomaConocimiento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TomadaConocimientoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ObraFinalizada = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Planificaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Planificaciones_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanificacionSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Anio = table.Column<int>(type: "int", nullable: false),
                    Mes = table.Column<int>(type: "int", nullable: false),
                    FechaTomaConocimiento = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TomadaConocimientoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanificacionSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanificacionSnapshots_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TablasPonderacion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TablasPonderacion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TablasPonderacion_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CertificadoEstructuras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CertificadoId = table.Column<int>(type: "int", nullable: false),
                    EstructuraCostosId = table.Column<int>(type: "int", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    Titulo = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    SubtotalAnterior = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    SubtotalActual = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    SubtotalAcumulado = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificadoEstructuras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CertificadoEstructuras_Certificados_CertificadoId",
                        column: x => x.CertificadoId,
                        principalTable: "Certificados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CertificadoEstructuras_EstructurasCostos_EstructuraCostosId",
                        column: x => x.EstructuraCostosId,
                        principalTable: "EstructurasCostos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemsEstructura",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EstructuraCostosId = table.Column<int>(type: "int", nullable: false),
                    TipoMovimiento = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ItemOrigenId = table.Column<int>(type: "int", nullable: true),
                    EsAgrupador = table.Column<bool>(type: "bit", nullable: false),
                    AgrupadorPadreId = table.Column<int>(type: "int", nullable: true),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Unidad = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Cantidad = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    PUBasico = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Monto = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemsEstructura", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemsEstructura_EstructurasCostos_EstructuraCostosId",
                        column: x => x.EstructuraCostosId,
                        principalTable: "EstructurasCostos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemsEstructura_ItemsEstructura_AgrupadorPadreId",
                        column: x => x.AgrupadorPadreId,
                        principalTable: "ItemsEstructura",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemsEstructura_ItemsEstructura_ItemOrigenId",
                        column: x => x.ItemOrigenId,
                        principalTable: "ItemsEstructura",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Autorizantes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanificacionId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: true),
                    Denominacion = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Autorizantes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Autorizantes_Planificaciones_PlanificacionId",
                        column: x => x.PlanificacionId,
                        principalTable: "Planificaciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanMontoSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanificacionSnapshotId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: true),
                    Denominacion = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Concepto = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Anio = table.Column<int>(type: "int", nullable: true),
                    Mes = table.Column<int>(type: "int", nullable: true),
                    Moneda = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanMontoSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanMontoSnapshots_PlanificacionSnapshots_PlanificacionSnapshotId",
                        column: x => x.PlanificacionSnapshotId,
                        principalTable: "PlanificacionSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemsPonderacion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TablaPonderacionId = table.Column<int>(type: "int", nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    Insumo = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    PesoPorcentaje = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    IndiceId = table.Column<int>(type: "int", nullable: false),
                    DescripcionINDEC = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemsPonderacion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemsPonderacion_IndicesINDEC_IndiceId",
                        column: x => x.IndiceId,
                        principalTable: "IndicesINDEC",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemsPonderacion_TablasPonderacion_TablaPonderacionId",
                        column: x => x.TablaPonderacionId,
                        principalTable: "TablasPonderacion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RedeterminacionesGuardadas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    TablaPonderacionId = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FechaGuardado = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MesBase = table.Column<int>(type: "int", nullable: false),
                    AnioBase = table.Column<int>(type: "int", nullable: false),
                    IdPublicacionBase = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    MesSalto = table.Column<int>(type: "int", nullable: false),
                    AnioSalto = table.Column<int>(type: "int", nullable: false),
                    IdPublicacionSalto = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    TotalKiK0 = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    PorcentajeAumento = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    VariacionAcumulada = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    NroDisparo = table.Column<int>(type: "int", nullable: false),
                    NroExpedienteVR = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    FechaAprobacionCCyR = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaAprobacionOS = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaLimitePresentacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedeterminacionesGuardadas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RedeterminacionesGuardadas_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RedeterminacionesGuardadas_TablasPonderacion_TablaPonderacionId",
                        column: x => x.TablaPonderacionId,
                        principalTable: "TablasPonderacion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemsCertificado",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CertificadoEstructuraId = table.Column<int>(type: "int", nullable: false),
                    ItemEstructuraId = table.Column<int>(type: "int", nullable: false),
                    TipoMovimiento = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PorcentajeActual = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    CantidadActual = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MontoActual = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PorcentajeAnterior = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    CantidadAnterior = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    MontoAnterior = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    PorcentajeAcumulado = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    CantidadAcumulada = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    MontoAcumulado = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CantidadContrato = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    PUBasico = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    MontoContrato = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Observaciones = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemsCertificado", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemsCertificado_CertificadoEstructuras_CertificadoEstructuraId",
                        column: x => x.CertificadoEstructuraId,
                        principalTable: "CertificadoEstructuras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemsCertificado_ItemsEstructura_ItemEstructuraId",
                        column: x => x.ItemEstructuraId,
                        principalTable: "ItemsEstructura",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanMontos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AutorizanteId = table.Column<int>(type: "int", nullable: false),
                    Concepto = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Anio = table.Column<int>(type: "int", nullable: true),
                    Mes = table.Column<int>(type: "int", nullable: true),
                    Moneda = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanMontos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanMontos_Autorizantes_AutorizanteId",
                        column: x => x.AutorizanteId,
                        principalTable: "Autorizantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RedeterminacionesGuardadasItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RedeterminacionGuardadaId = table.Column<int>(type: "int", nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    Insumo = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    PesoPorcentaje = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    ValorMesBase = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ValorMesSalto = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    KiK0 = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    VariacionPonderada = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    DescripcionINDEC = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ItemPonderacionId = table.Column<int>(type: "int", nullable: true),
                    IndiceId = table.Column<int>(type: "int", nullable: true),
                    ValorIndiceBaseId = table.Column<int>(type: "int", nullable: true),
                    ValorIndiceSaltoId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedeterminacionesGuardadasItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RedeterminacionesGuardadasItems_IndicesINDEC_IndiceId",
                        column: x => x.IndiceId,
                        principalTable: "IndicesINDEC",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RedeterminacionesGuardadasItems_ItemsPonderacion_ItemPonderacionId",
                        column: x => x.ItemPonderacionId,
                        principalTable: "ItemsPonderacion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RedeterminacionesGuardadasItems_RedeterminacionesGuardadas_RedeterminacionGuardadaId",
                        column: x => x.RedeterminacionGuardadaId,
                        principalTable: "RedeterminacionesGuardadas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RedeterminacionesGuardadasItems_ValoresIndice_ValorIndiceBaseId",
                        column: x => x.ValorIndiceBaseId,
                        principalTable: "ValoresIndice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RedeterminacionesGuardadasItems_ValoresIndice_ValorIndiceSaltoId",
                        column: x => x.ValorIndiceSaltoId,
                        principalTable: "ValoresIndice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "IndicesINDEC",
                columns: new[] { "Id", "CodigoIndice", "CuadroReferencia", "FamiliaRecurso", "IncisoCode", "IndiceNormalizado" },
                values: new object[,]
                {
                    { 1, "ALBANILERIA", "Cuadro 1.5", "Albañilería / materiales generales de obra", "b", "B) Albañilería" },
                    { 2, "ALQ_CAMION_VOLCADOR", "71240 - 11", "Alquiler de camión volcador", null, null },
                    { 3, "ALQ_PALA_CARGADORA", "51800 - 11", "Alquiler de pala cargadora", null, null },
                    { 4, "ALQ_RETROEXCAVADORA", "51800 - 21", "Alquiler de retroexcavadora", null, null },
                    { 5, "ALUMINIO", "2720 41530-1", "Aluminio", null, null },
                    { 6, "ANDAMIOS", "Cuadro 1.6", "Andamios", "f", "f) Andamios" },
                    { 7, "BALASTOS", "3150 46539-1", "Balastos", null, null },
                    { 8, "CAMIONES_CHASIS", null, "Camiones y transporte pesado", "(6)", "(6) 3410 49115-2" },
                    { 9, "CARP_METAL_HERR", "Cuadro 1.5", "Carpintería metálica, herrería y señalética física", "d", "d) Carpinterías" },
                    { 10, "PVC", "Cuadro 1.9", "Caños de PVC / plásticos para instalaciones", "h", "h) Caños de PVC para instalaciones varias" },
                    { 11, "CAÑO_ACERO_INST_ELECTRICA", "41277 - 21", "Caños de acero para instalación eléctrica", null, null },
                    { 12, "CEMENTO_CAL", "2694", "Cemento y cal", null, null },
                    { 13, "CERAMICOS_BALDOSAS", null, "Cerámicos, baldosas y losas", "(1)", "(1) 37370" },
                    { 14, "CHAPAS_METALICAS", "2899 42999-2", "Chapas metálicas y productos metálicos elaborados", null, null },
                    { 15, "COMB_LUB", "Cuadro 3.2-23", "Combustibles, asfaltos y lubricantes", "k", "k) Asfaltos, combustibles y lubricantes" },
                    { 16, "ELECTROBOMBA", null, "Electrobombas", "v", "v) Electrobomba" },
                    { 17, "GASTOS_GENERALES", "Cuadro 1.4", "Gastos generales / subcontratos / servicios auxiliares", "p", "p) Gastos generales" },
                    { 18, "HERRAMIENTAS_MANO", "2893 42921-2", "Herramientas de mano", null, null },
                    { 19, "HIDROFUGOS", "2699 37990-1", "Hidrófugos", null, null },
                    { 20, "ACERO_HIERRO", null, "Hierros, aceros y perfiles básicos", "(2)", "(2) 2710 27101" },
                    { 21, "HORMIGON", "Cuadro 1.9", "Hormigón elaborado y premoldeados", "s", "s) Hormigón" },
                    { 22, "IMPERMEABILIZANTES", "2422 35110-5", "Impermeabilizantes químicos", null, null },
                    { 23, "INST_ELECTRICA", "Cuadro 1.5", "Instalación eléctrica, iluminación y cableado", "g", "g) Artefactos de iluminación y cableado" },
                    { 24, "SANITARIA_INCENDIO", "Cuadro 1.5", "Instalación sanitaria y contra incendio", "r", "r) Artefactos para baño y grifería" },
                    { 25, "JABALINA", "42999 - 51", "Jabalinas y puesta a tierra", null, null },
                    { 26, "LADRILLOS", "2693 37350-1", "Ladrillos y mampuestos cerámicos", null, null },
                    { 27, "MADERA", null, "Madera y carpintería de madera", null, "Var 8.1.2 Carpintería de madera" },
                    { 28, "MANO_OBRA", "Cuadro 1.4", "Mano de obra", "a", "a) Mano de obra" },
                    { 29, "MEMBRANAS_PLASTICOS", "Cuadro 3.2-252", "Membranas, impermeabilizantes plásticos y geosintéticos", "w", "w) Membrana impermeabilizante / Productos de plástico" },
                    { 30, "MOSAICO_GRANITICO", "37540 - 11", "Mosaico / granito / marmolería", null, null },
                    { 31, "MOTORES_ELECTRICOS_AA", "Cuadro 3.2-31", "Motores eléctricos y equipos electromecánicos / AA", "i", "i) Motores eléctricos y equipos de aire acondicionado" },
                    { 32, "MAQUINAS_HERRAMIENTAS", "2922 29221", "Máquinas herramientas y accesorios", null, null },
                    { 33, "PIEDRAS_ARENAS_ARCILLAS", null, "Piedras, arenas y arcillas", "(29)", "(29) 1410 14101" },
                    { 34, "PINTURA", "51730 - 1", "Pinturas y solventes", null, null },
                    { 35, "PISOS_REVEST", null, "Pisos y revestimientos", "c", "c) Pisos y revestimientos" },
                    { 36, "TAPA_CHAPA_CAMARA", "37560 - 21", "Tapas de chapa para cámaras", null, null },
                    { 37, "VIDRIOS", null, "Vidrios, espejos y blindex", "(21)", "(21) 2610 26101" },
                    { 38, "VALVULAS_BRONCE", null, "Válvulas de bronce", "u", "u) Válvulas de bronce" },
                    { 39, "ZOCALO_GRANITICO", "37540 - 21", "Zócalos graníticos", null, null },
                    { 40, "COSTO_FINANCIERO", null, "Costo Financiero – Tasas Activas Banco Nación", null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Autorizantes_PlanificacionId",
                table: "Autorizantes",
                column: "PlanificacionId");

            migrationBuilder.CreateIndex(
                name: "IX_Autorizantes_PlanificacionId_Tipo_Numero",
                table: "Autorizantes",
                columns: new[] { "PlanificacionId", "Tipo", "Numero" },
                unique: true,
                filter: "[Numero] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Autorizantes_PlanificacionId_Tipo_SinNumero",
                table: "Autorizantes",
                columns: new[] { "PlanificacionId", "Tipo" },
                unique: true,
                filter: "[Numero] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CertificadoEstructuras_CertificadoId_EstructuraCostosId",
                table: "CertificadoEstructuras",
                columns: new[] { "CertificadoId", "EstructuraCostosId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CertificadoEstructuras_EstructuraCostosId",
                table: "CertificadoEstructuras",
                column: "EstructuraCostosId");

            migrationBuilder.CreateIndex(
                name: "IX_Certificados_ObraId_Numero",
                table: "Certificados",
                columns: new[] { "ObraId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DigestsPlanificacionEnviados_Anio_Mes",
                table: "DigestsPlanificacionEnviados",
                columns: new[] { "Anio", "Mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EstructurasCostos_ObraId",
                table: "EstructurasCostos",
                column: "ObraId");

            migrationBuilder.CreateIndex(
                name: "IX_IndicesINDEC_CodigoIndice",
                table: "IndicesINDEC",
                column: "CodigoIndice",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemsCertificado_CertificadoEstructuraId_ItemEstructuraId",
                table: "ItemsCertificado",
                columns: new[] { "CertificadoEstructuraId", "ItemEstructuraId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemsCertificado_ItemEstructuraId",
                table: "ItemsCertificado",
                column: "ItemEstructuraId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemsEstructura_AgrupadorPadreId",
                table: "ItemsEstructura",
                column: "AgrupadorPadreId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemsEstructura_EstructuraCostosId",
                table: "ItemsEstructura",
                column: "EstructuraCostosId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemsEstructura_ItemOrigenId",
                table: "ItemsEstructura",
                column: "ItemOrigenId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemsPonderacion_IndiceId",
                table: "ItemsPonderacion",
                column: "IndiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemsPonderacion_TablaPonderacionId_Numero",
                table: "ItemsPonderacion",
                columns: new[] { "TablaPonderacionId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Obras_DirectorUsuarioId",
                table: "Obras",
                column: "DirectorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Planificaciones_ObraId",
                table: "Planificaciones",
                column: "ObraId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanificacionSnapshots_ObraId_Anio_Mes",
                table: "PlanificacionSnapshots",
                columns: new[] { "ObraId", "Anio", "Mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanMontos_AutorizanteId_Concepto_Moneda_Anio_Mes",
                table: "PlanMontos",
                columns: new[] { "AutorizanteId", "Concepto", "Moneda", "Anio", "Mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanMontoSnapshots_PlanificacionSnapshotId",
                table: "PlanMontoSnapshots",
                column: "PlanificacionSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_RedeterminacionesGuardadas_ObraId_NroDisparo",
                table: "RedeterminacionesGuardadas",
                columns: new[] { "ObraId", "NroDisparo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RedeterminacionesGuardadas_TablaPonderacionId",
                table: "RedeterminacionesGuardadas",
                column: "TablaPonderacionId");

            migrationBuilder.CreateIndex(
                name: "IX_RedeterminacionesGuardadasItems_IndiceId",
                table: "RedeterminacionesGuardadasItems",
                column: "IndiceId");

            migrationBuilder.CreateIndex(
                name: "IX_RedeterminacionesGuardadasItems_ItemPonderacionId",
                table: "RedeterminacionesGuardadasItems",
                column: "ItemPonderacionId");

            migrationBuilder.CreateIndex(
                name: "IX_RedeterminacionesGuardadasItems_RedeterminacionGuardadaId",
                table: "RedeterminacionesGuardadasItems",
                column: "RedeterminacionGuardadaId");

            migrationBuilder.CreateIndex(
                name: "IX_RedeterminacionesGuardadasItems_ValorIndiceBaseId",
                table: "RedeterminacionesGuardadasItems",
                column: "ValorIndiceBaseId");

            migrationBuilder.CreateIndex(
                name: "IX_RedeterminacionesGuardadasItems_ValorIndiceSaltoId",
                table: "RedeterminacionesGuardadasItems",
                column: "ValorIndiceSaltoId");

            migrationBuilder.CreateIndex(
                name: "IX_RolloversPlanificacionEjecutados_Anio_Mes",
                table: "RolloversPlanificacionEjecutados",
                columns: new[] { "Anio", "Mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TablasPonderacion_ObraId",
                table: "TablasPonderacion",
                column: "ObraId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_WindowsUser",
                table: "Usuarios",
                column: "WindowsUser",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosRoles_UsuarioId_Rol",
                table: "UsuariosRoles",
                columns: new[] { "UsuarioId", "Rol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValoresIndice_IndiceId_Anio_Mes",
                table: "ValoresIndice",
                columns: new[] { "IndiceId", "Anio", "Mes" });

            migrationBuilder.CreateIndex(
                name: "IX_ValoresIndice_IndiceId_Anio_Mes_IdPublicacion",
                table: "ValoresIndice",
                columns: new[] { "IndiceId", "Anio", "Mes", "IdPublicacion" },
                unique: true,
                filter: "[IdPublicacion] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ValoresIndice_IndiceId_Anio_Mes_SinPublicacion",
                table: "ValoresIndice",
                columns: new[] { "IndiceId", "Anio", "Mes" },
                unique: true,
                filter: "[IdPublicacion] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DigestsPlanificacionEnviados");

            migrationBuilder.DropTable(
                name: "ItemsCertificado");

            migrationBuilder.DropTable(
                name: "PlanMontos");

            migrationBuilder.DropTable(
                name: "PlanMontoSnapshots");

            migrationBuilder.DropTable(
                name: "RedeterminacionesGuardadasItems");

            migrationBuilder.DropTable(
                name: "RolloversPlanificacionEjecutados");

            migrationBuilder.DropTable(
                name: "UsuariosRoles");

            migrationBuilder.DropTable(
                name: "CertificadoEstructuras");

            migrationBuilder.DropTable(
                name: "ItemsEstructura");

            migrationBuilder.DropTable(
                name: "Autorizantes");

            migrationBuilder.DropTable(
                name: "PlanificacionSnapshots");

            migrationBuilder.DropTable(
                name: "ItemsPonderacion");

            migrationBuilder.DropTable(
                name: "RedeterminacionesGuardadas");

            migrationBuilder.DropTable(
                name: "ValoresIndice");

            migrationBuilder.DropTable(
                name: "Certificados");

            migrationBuilder.DropTable(
                name: "EstructurasCostos");

            migrationBuilder.DropTable(
                name: "Planificaciones");

            migrationBuilder.DropTable(
                name: "TablasPonderacion");

            migrationBuilder.DropTable(
                name: "IndicesINDEC");

            migrationBuilder.DropTable(
                name: "Obras");

            migrationBuilder.DropTable(
                name: "Usuarios");
        }
    }
}
