using Microsoft.EntityFrameworkCore;
using SIGO.Models;
using SIGO.Services;

namespace SIGO.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser? currentUser = null) : DbContext(options)
{
    private void ApplyAudit()
    {
        var now = DateTime.UtcNow;
        var uid = currentUser?.UserId;
        foreach (var e in ChangeTracker.Entries<BaseEntity>())
        {
            if (e.State == EntityState.Added)
            {
                e.Entity.CreatedAt = now;
                e.Entity.CreatedByUserId = uid;
            }
            else if (e.State == EntityState.Modified)
            {
                e.Entity.UpdatedAt = now;
                e.Entity.UpdatedByUserId = uid;
            }
        }
    }

    public override int SaveChanges()
    {
        ApplyAudit();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAudit();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAudit();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public DbSet<Obra> Obras => Set<Obra>();
    public DbSet<TablaPonderacion> TablasPonderacion => Set<TablaPonderacion>();
    public DbSet<ItemPonderacion> ItemsPonderacion => Set<ItemPonderacion>();
    public DbSet<IndiceINDEC> IndicesINDEC => Set<IndiceINDEC>();
    public DbSet<ValorIndice> ValoresIndice => Set<ValorIndice>();
    public DbSet<RedeterminacionGuardada> RedeterminacionesGuardadas => Set<RedeterminacionGuardada>();
    public DbSet<RedeterminacionGuardadaItem> RedeterminacionesGuardadasItems => Set<RedeterminacionGuardadaItem>();
    public DbSet<EstructuraCostos> EstructurasCostos => Set<EstructuraCostos>();
    public DbSet<ItemEstructura> ItemsEstructura => Set<ItemEstructura>();
    public DbSet<Certificado> Certificados => Set<Certificado>();
    public DbSet<CertificadoEstructura> CertificadoEstructuras => Set<CertificadoEstructura>();
    public DbSet<ItemCertificado> ItemsCertificado => Set<ItemCertificado>();

    // ── Módulo Planificación (contexto separado; solo comparte Obra) ──────────────
    public DbSet<Planificacion> Planificaciones => Set<Planificacion>();
    public DbSet<Autorizante> Autorizantes => Set<Autorizante>();
    public DbSet<PlanMonto> PlanMontos => Set<PlanMonto>();
    public DbSet<DigestPlanificacionEnviado> DigestsPlanificacionEnviados => Set<DigestPlanificacionEnviado>();
    public DbSet<PlanificacionSnapshot> PlanificacionSnapshots => Set<PlanificacionSnapshot>();
    public DbSet<PlanMontoSnapshot> PlanMontoSnapshots => Set<PlanMontoSnapshot>();
    public DbSet<RolloverPlanificacionEjecutado> RolloversPlanificacionEjecutados => Set<RolloverPlanificacionEjecutado>();

    // ── Seguridad (autorización; la autenticación es Windows/Negotiate) ───────────
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<UsuarioRol> UsuariosRoles => Set<UsuarioRol>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Los strings dejan de ser nvarchar(max) por defecto: 250 salvo override
        // (columnas indexables y sin ALTER masivo el día que se agregue Identity).
        configurationBuilder.Properties<string>().HaveMaxLength(250);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Textos largos que sí necesitan más que la convención de 250.
        modelBuilder.Entity<Obra>().Property(o => o.CorreoDirectorObra).HasMaxLength(500);
        modelBuilder.Entity<ItemEstructura>().Property(i => i.Descripcion).HasMaxLength(500);
        modelBuilder.Entity<ItemPonderacion>().Property(i => i.DescripcionINDEC).HasMaxLength(500);
        modelBuilder.Entity<RedeterminacionGuardadaItem>().Property(i => i.DescripcionINDEC).HasMaxLength(500);
        modelBuilder.Entity<Certificado>().Property(c => c.Observaciones).HasMaxLength(2000);
        modelBuilder.Entity<ItemCertificado>().Property(i => i.Observaciones).HasMaxLength(2000);
        modelBuilder.Entity<EstructuraCostos>().Property(e => e.Observaciones).HasMaxLength(2000);
        modelBuilder.Entity<Planificacion>().Property(p => p.MotivoRevision).HasMaxLength(2000);
        modelBuilder.Entity<Planificacion>().Property(p => p.Correcciones).HasMaxLength(2000);

        // Auditoría lista para Identity: mismo largo que AspNetUsers.Id (450).
        foreach (var et in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(BaseEntity).IsAssignableFrom(t.ClrType)).ToList())
        {
            modelBuilder.Entity(et.ClrType).Property(nameof(BaseEntity.CreatedByUserId)).HasMaxLength(450);
            modelBuilder.Entity(et.ClrType).Property(nameof(BaseEntity.UpdatedByUserId)).HasMaxLength(450);
        }

        modelBuilder.Entity<ItemPonderacion>()
            .Property(x => x.PesoPorcentaje)
            .HasPrecision(7, 4);

        modelBuilder.Entity<RedeterminacionGuardadaItem>()
            .Property(x => x.PesoPorcentaje).HasPrecision(7, 4);
        modelBuilder.Entity<RedeterminacionGuardadaItem>()
            .Property(x => x.ValorMesBase).HasPrecision(18, 4);
        modelBuilder.Entity<RedeterminacionGuardadaItem>()
            .Property(x => x.ValorMesSalto).HasPrecision(18, 4);
        modelBuilder.Entity<RedeterminacionGuardadaItem>()
            .Property(x => x.KiK0).HasPrecision(18, 6);
        modelBuilder.Entity<RedeterminacionGuardadaItem>()
            .Property(x => x.VariacionPonderada).HasPrecision(18, 6);
        modelBuilder.Entity<RedeterminacionGuardada>()
            .Property(x => x.TotalKiK0).HasPrecision(18, 6);
        modelBuilder.Entity<RedeterminacionGuardada>()
            .Property(x => x.PorcentajeAumento).HasPrecision(18, 4);
        modelBuilder.Entity<RedeterminacionGuardada>()
            .Property(x => x.VariacionAcumulada).HasPrecision(18, 6);
        modelBuilder.Entity<RedeterminacionGuardada>()
            .HasOne(r => r.TablaPonderacion)
            .WithMany()
            .HasForeignKey(r => r.TablaPonderacionId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RedeterminacionGuardada>()
            .Property(r => r.Estado).HasConversion<string>().HasMaxLength(20);

        // Nro de disparo único por obra (la asignación Count+1 en UI duplicaba números
        // tras una eliminación; el índice hace imposible persistir el duplicado).
        modelBuilder.Entity<RedeterminacionGuardada>()
            .HasIndex(r => new { r.ObraId, r.NroDisparo })
            .IsUnique();

        // Trazabilidad: FK sin navegación, todas Restrict (catálogo no se borra en cascada)
        modelBuilder.Entity<RedeterminacionGuardadaItem>()
            .HasOne<ItemPonderacion>().WithMany()
            .HasForeignKey(x => x.ItemPonderacionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RedeterminacionGuardadaItem>()
            .HasOne<IndiceINDEC>().WithMany()
            .HasForeignKey(x => x.IndiceId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RedeterminacionGuardadaItem>()
            .HasOne<ValorIndice>().WithMany()
            .HasForeignKey(x => x.ValorIndiceBaseId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RedeterminacionGuardadaItem>()
            .HasOne<ValorIndice>().WithMany()
            .HasForeignKey(x => x.ValorIndiceSaltoId)
            .OnDelete(DeleteBehavior.Restrict);

        // Una obra → una sola tabla de ponderación
        modelBuilder.Entity<TablaPonderacion>()
            .HasIndex(t => t.ObraId)
            .IsUnique();

        // ── Protección del historial contractual ──────────────────────────────────
        // Una obra con certificados, estructuras, ponderaciones o redeterminaciones no
        // puede borrarse (sin esto la convención generaba Cascade: borrar la obra
        // destruía silenciosamente todo su historial).
        modelBuilder.Entity<Certificado>()
            .HasOne(c => c.Obra)
            .WithMany()
            .HasForeignKey(c => c.ObraId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<EstructuraCostos>()
            .HasOne(e => e.Obra)
            .WithMany(o => o.EstructurasCostos)
            .HasForeignKey(e => e.ObraId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TablaPonderacion>()
            .HasOne(t => t.Obra)
            .WithMany(o => o.TablasPonderacion)
            .HasForeignKey(t => t.ObraId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RedeterminacionGuardada>()
            .HasOne(r => r.Obra)
            .WithMany()
            .HasForeignKey(r => r.ObraId)
            .OnDelete(DeleteBehavior.Restrict);

        // Un índice INDEC referenciado por ítems de ponderación tampoco puede borrarse
        // (la cascada alteraba los pesos contractuales al limpiar el catálogo).
        modelBuilder.Entity<ItemPonderacion>()
            .HasOne(i => i.Indice)
            .WithMany()
            .HasForeignKey(i => i.IndiceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Backstop del autonumerado Max+1 de PonderacionEditar: dos altas concurrentes
        // no pueden quedar con el mismo número de insumo dentro de la tabla.
        modelBuilder.Entity<ItemPonderacion>()
            .HasIndex(i => new { i.TablaPonderacionId, i.Numero })
            .IsUnique();

        modelBuilder.Entity<ValorIndice>()
            .Property(x => x.Valor)
            .HasPrecision(18, 4);

        modelBuilder.Entity<ValorIndice>()
            .HasIndex(x => new { x.IndiceId, x.Anio, x.Mes, x.IdPublicacion })
            .IsUnique();

        // El índice anterior se filtra a IdPublicacion NOT NULL (SQL Server trata cada
        // NULL como distinto): sin este segundo único, las filas sin publicación
        // (tasas Banco Nación) admitían duplicados del mismo mes.
        // Nota: los dos índices siguientes van sobre las mismas columnas, por eso
        // usan la sobrecarga con nombre de HasIndex — sin nombre, EF devuelve el
        // mismo builder y el segundo pisaría al primero en vez de crear otro índice.
        modelBuilder.Entity<ValorIndice>()
            .HasIndex(x => new { x.IndiceId, x.Anio, x.Mes },
                      "IX_ValoresIndice_IndiceId_Anio_Mes_SinPublicacion")
            .IsUnique()
            .HasFilter("[IdPublicacion] IS NULL");

        // Los dos únicos anteriores están filtrados por IdPublicacion, así que SQL
        // Server no puede usarlos como soporte de la FK ni para consultas por
        // (IndiceId, Anio, Mes) sin predicado de publicación: este índice plano
        // (sin filtro) cubre joins, cascadas y esas búsquedas.
        modelBuilder.Entity<ValorIndice>()
            .HasIndex(x => new { x.IndiceId, x.Anio, x.Mes },
                      "IX_ValoresIndice_IndiceId_Anio_Mes");

        // ── EstructuraCostos / ItemEstructura ─────────────────────────────────────
        modelBuilder.Entity<ItemEstructura>()
            .Property(x => x.Cantidad).HasPrecision(18, 4);
        modelBuilder.Entity<ItemEstructura>()
            .Property(x => x.PUBasico).HasPrecision(18, 4);
        modelBuilder.Entity<ItemEstructura>()
            .Property(x => x.Monto).HasPrecision(18, 4);

        // Self-referencial: padre → hijos, sin cascade para evitar ciclos
        modelBuilder.Entity<ItemEstructura>()
            .HasOne(i => i.AgrupadorPadre)
            .WithMany(i => i.Hijos)
            .HasForeignKey(i => i.AgrupadorPadreId)
            .OnDelete(DeleteBehavior.Restrict);

        // Segunda self-reference: ítem que afecta a un ítem original (BED/economía/compensación)
        modelBuilder.Entity<ItemEstructura>()
            .HasOne(i => i.ItemOrigen)
            .WithMany()
            .HasForeignKey(i => i.ItemOrigenId)
            .OnDelete(DeleteBehavior.Restrict);

        // Enums persistidos como string
        modelBuilder.Entity<EstructuraCostos>()
            .Property(e => e.Tipo).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<ItemEstructura>()
            .Property(i => i.TipoMovimiento).HasConversion<string>().HasMaxLength(20);

        // ── Certificados ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Certificado>()
            .Property(c => c.Estado).HasConversion<string>().HasMaxLength(20);

        // Número de certificado único por obra (el chequeo en UI no es atómico:
        // dos altas simultáneas pasaban la validación y duplicaban el número).
        modelBuilder.Entity<Certificado>()
            .HasIndex(c => new { c.ObraId, c.Numero })
            .IsUnique();

        // Bloques: Certificado → CertificadoEstructura (cascade), → EstructuraCostos (restrict)
        modelBuilder.Entity<CertificadoEstructura>()
            .HasOne(ce => ce.Certificado)
            .WithMany(c => c.Estructuras)
            .HasForeignKey(ce => ce.CertificadoId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CertificadoEstructura>()
            .HasOne(ce => ce.EstructuraCostos)
            .WithMany()
            .HasForeignKey(ce => ce.EstructuraCostosId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CertificadoEstructura>()
            .HasIndex(ce => new { ce.CertificadoId, ce.EstructuraCostosId })
            .IsUnique();
        modelBuilder.Entity<CertificadoEstructura>(b =>
        {
            b.Property(x => x.SubtotalAnterior).HasPrecision(18, 4);
            b.Property(x => x.SubtotalActual).HasPrecision(18, 4);
            b.Property(x => x.SubtotalAcumulado).HasPrecision(18, 4);
        });

        // Ítems: CertificadoEstructura → ItemCertificado (cascade), → ItemEstructura (restrict)
        modelBuilder.Entity<ItemCertificado>()
            .HasOne(ic => ic.CertificadoEstructura)
            .WithMany(ce => ce.Items)
            .HasForeignKey(ic => ic.CertificadoEstructuraId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ItemCertificado>()
            .HasOne(ic => ic.ItemEstructura)
            .WithMany()
            .HasForeignKey(ic => ic.ItemEstructuraId)
            .OnDelete(DeleteBehavior.Restrict);

        // Backstop de la unicidad que GuardarItemsAsync mantiene en memoria: un ítem
        // de estructura no puede aparecer dos veces en el mismo bloque del certificado
        // (dos guardados concurrentes podían duplicar renglones y montos).
        modelBuilder.Entity<ItemCertificado>()
            .HasIndex(ic => new { ic.CertificadoEstructuraId, ic.ItemEstructuraId })
            .IsUnique();
        modelBuilder.Entity<ItemCertificado>(b =>
        {
            b.Property(i => i.TipoMovimiento).HasConversion<string>().HasMaxLength(20);
            b.Property(i => i.CantidadActual).HasPrecision(18, 4);
            b.Property(i => i.MontoActual).HasPrecision(18, 4);
            b.Property(i => i.CantidadAnterior).HasPrecision(18, 4);
            b.Property(i => i.MontoAnterior).HasPrecision(18, 4);
            b.Property(i => i.CantidadAcumulada).HasPrecision(18, 4);
            b.Property(i => i.MontoAcumulado).HasPrecision(18, 4);
            b.Property(i => i.PorcentajeActual).HasPrecision(9, 4);
            b.Property(i => i.PorcentajeAnterior).HasPrecision(9, 4);
            b.Property(i => i.PorcentajeAcumulado).HasPrecision(9, 4);
            b.Property(i => i.CantidadContrato).HasPrecision(18, 4);
            b.Property(i => i.PUBasico).HasPrecision(18, 4);
            b.Property(i => i.MontoContrato).HasPrecision(18, 4);
        });

        // ── Planificación (contexto separado; única FK a lo compartido: Obra) ──────
        // Un solo plan editable por obra.
        modelBuilder.Entity<Planificacion>()
            .HasIndex(p => p.ObraId)
            .IsUnique();
        modelBuilder.Entity<Planificacion>()
            .HasOne(p => p.Obra)
            .WithMany()
            .HasForeignKey(p => p.ObraId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Planificacion>()
            .Property(p => p.Estado).HasConversion<string>().HasMaxLength(20);

        // Planificacion → Autorizante (cascade)
        modelBuilder.Entity<Autorizante>()
            .HasOne(a => a.Planificacion)
            .WithMany(p => p.Autorizantes)
            .HasForeignKey(a => a.PlanificacionId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Autorizante>()
            .Property(a => a.Tipo).HasConversion<string>().HasMaxLength(20);

        // Backstop de la unicidad que el servicio valida en memoria: sin "Adicional N°1"
        // duplicado por dos altas concurrentes. EF filtra el único a Numero NOT NULL,
        // así que la Básica (Numero NULL) necesita su propio único filtrado (mismo
        // patrón que ValorIndice sin publicación).
        modelBuilder.Entity<Autorizante>()
            .HasIndex(a => new { a.PlanificacionId, a.Tipo, a.Numero })
            .IsUnique();
        modelBuilder.Entity<Autorizante>()
            .HasIndex(a => new { a.PlanificacionId, a.Tipo })
            .IsUnique()
            .HasFilter("[Numero] IS NULL")
            .HasDatabaseName("IX_Autorizantes_PlanificacionId_Tipo_SinNumero");

        // Al crear el único compuesto, EF dio de baja el índice simple de la FK por
        // considerarlo redundante — pero el compuesto queda filtrado a Numero NOT NULL
        // y no sirve para joins/cascadas genéricas por PlanificacionId. Se restituye
        // explícito y sin filtro.
        modelBuilder.Entity<Autorizante>()
            .HasIndex(a => a.PlanificacionId)
            .HasDatabaseName("IX_Autorizantes_PlanificacionId");

        // Un digest por mes: el índice único es el backstop de idempotencia incluso
        // con dos instancias de la app corriendo.
        modelBuilder.Entity<DigestPlanificacionEnviado>()
            .HasIndex(d => new { d.Anio, d.Mes })
            .IsUnique();

        // ── Snapshots de planificación (toma de conocimiento de Presupuesto) ───────
        modelBuilder.Entity<Planificacion>()
            .Property(p => p.TomadaConocimientoPor).HasMaxLength(100);

        // Una versión por obra y mes; una nueva toma en el mismo mes la reemplaza
        // (el servicio borra y recrea; el único es el backstop ante concurrencia).
        modelBuilder.Entity<PlanificacionSnapshot>(b =>
        {
            b.HasIndex(s => new { s.ObraId, s.Anio, s.Mes }).IsUnique();
            b.HasOne(s => s.Obra)
                .WithMany()
                .HasForeignKey(s => s.ObraId)
                .OnDelete(DeleteBehavior.Restrict);
            b.Property(s => s.TomadaConocimientoPor).HasMaxLength(100);
        });

        modelBuilder.Entity<PlanMontoSnapshot>(b =>
        {
            b.HasOne(m => m.Snapshot)
                .WithMany(s => s.Montos)
                .HasForeignKey(m => m.PlanificacionSnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
            b.Property(m => m.Tipo).HasConversion<string>().HasMaxLength(20);
            b.Property(m => m.Concepto).HasConversion<string>().HasMaxLength(20);
            b.Property(m => m.Moneda).HasConversion<string>().HasMaxLength(10);
            b.Property(m => m.Monto).HasPrecision(18, 4);
        });

        // Un rollover por mes (idempotencia, mismo patrón que el digest).
        modelBuilder.Entity<RolloverPlanificacionEjecutado>()
            .HasIndex(r => new { r.Anio, r.Mes })
            .IsUnique();

        // ── Usuarios y roles ──────────────────────────────────────────────────────
        modelBuilder.Entity<Usuario>()
            .HasIndex(u => u.WindowsUser)
            .IsUnique();
        modelBuilder.Entity<UsuarioRol>()
            .HasOne(r => r.Usuario)
            .WithMany(u => u.Roles)
            .HasForeignKey(r => r.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UsuarioRol>()
            .Property(r => r.Rol).HasConversion<string>().HasMaxLength(20);
        modelBuilder.Entity<UsuarioRol>()
            .HasIndex(r => new { r.UsuarioId, r.Rol })
            .IsUnique();

        // Obra → director (usuario): Restrict, un usuario con obras asignadas no se borra.
        modelBuilder.Entity<Obra>()
            .HasOne(o => o.DirectorUsuario)
            .WithMany()
            .HasForeignKey(o => o.DirectorUsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        // Autorizante → PlanMonto (cascade)
        modelBuilder.Entity<PlanMonto>()
            .HasOne(m => m.Autorizante)
            .WithMany(a => a.Montos)
            .HasForeignKey(m => m.AutorizanteId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlanMonto>(b =>
        {
            b.Property(m => m.Concepto).HasConversion<string>().HasMaxLength(20);
            b.Property(m => m.Moneda).HasConversion<string>().HasMaxLength(10);
            b.Property(m => m.Monto).HasPrecision(18, 4);

            // Una sola celda por bloque × concepto × moneda × período. HasFilter(null)
            // anula el filtro "NOT NULL" que EF agrega por convención sobre columnas
            // nullable: acá se aprovecha que SQL Server trata los NULL como iguales en
            // índices únicos, así también hay una sola fila para los conceptos sin
            // período (MontoAutorizado/Anticipo: Anio y Mes NULL) y para los anuales
            // (CalculoAnual: Mes NULL).
            b.HasIndex(m => new { m.AutorizanteId, m.Concepto, m.Moneda, m.Anio, m.Mes })
                .IsUnique()
                .HasFilter(null);
        });

        // CodigoIndice es la clave de negocio del catálogo: acotada e única.
        modelBuilder.Entity<IndiceINDEC>()
            .Property(x => x.CodigoIndice).HasMaxLength(50);
        modelBuilder.Entity<IndiceINDEC>()
            .HasIndex(x => x.CodigoIndice)
            .IsUnique();

        // ── Índices INDEC ─────────────────────────────────────────────────────────
        // Fuente: INDEC Informa – Anexo Cuadro 5 (Decreto 1295/2002)
        modelBuilder.Entity<IndiceINDEC>().HasData(
            new IndiceINDEC { Id =  1, CodigoIndice = "ALBANILERIA",              FamiliaRecurso = "Albañilería / materiales generales de obra",                    IndiceNormalizado = "B) Albañilería",                                              CuadroReferencia = "Cuadro 1.5",      IncisoCode = "b"    },
            new IndiceINDEC { Id =  2, CodigoIndice = "ALQ_CAMION_VOLCADOR",       FamiliaRecurso = "Alquiler de camión volcador",                                  IndiceNormalizado = null,                                                          CuadroReferencia = "71240 - 11",      IncisoCode = null   },
            new IndiceINDEC { Id =  3, CodigoIndice = "ALQ_PALA_CARGADORA",        FamiliaRecurso = "Alquiler de pala cargadora",                                   IndiceNormalizado = null,                                                          CuadroReferencia = "51800 - 11",      IncisoCode = null   },
            new IndiceINDEC { Id =  4, CodigoIndice = "ALQ_RETROEXCAVADORA",       FamiliaRecurso = "Alquiler de retroexcavadora",                                  IndiceNormalizado = null,                                                          CuadroReferencia = "51800 - 21",      IncisoCode = null   },
            new IndiceINDEC { Id =  5, CodigoIndice = "ALUMINIO",                  FamiliaRecurso = "Aluminio",                                                     IndiceNormalizado = null,                                                          CuadroReferencia = "2720 41530-1",    IncisoCode = null   },
            new IndiceINDEC { Id =  6, CodigoIndice = "ANDAMIOS",                  FamiliaRecurso = "Andamios",                                                     IndiceNormalizado = "f) Andamios",                                                 CuadroReferencia = "Cuadro 1.6",      IncisoCode = "f"    },
            new IndiceINDEC { Id =  7, CodigoIndice = "BALASTOS",                  FamiliaRecurso = "Balastos",                                                     IndiceNormalizado = null,                                                          CuadroReferencia = "3150 46539-1",    IncisoCode = null   },
            new IndiceINDEC { Id =  8, CodigoIndice = "CAMIONES_CHASIS",           FamiliaRecurso = "Camiones y transporte pesado",                                 IndiceNormalizado = "(6) 3410 49115-2",                                            CuadroReferencia = null,              IncisoCode = "(6)"  },
            new IndiceINDEC { Id =  9, CodigoIndice = "CARP_METAL_HERR",           FamiliaRecurso = "Carpintería metálica, herrería y señalética física",           IndiceNormalizado = "d) Carpinterías",                                             CuadroReferencia = "Cuadro 1.5",      IncisoCode = "d"    },
            new IndiceINDEC { Id = 10, CodigoIndice = "PVC",                       FamiliaRecurso = "Caños de PVC / plásticos para instalaciones",                 IndiceNormalizado = "h) Caños de PVC para instalaciones varias",                   CuadroReferencia = "Cuadro 1.9",      IncisoCode = "h"    },
            new IndiceINDEC { Id = 11, CodigoIndice = "CAÑO_ACERO_INST_ELECTRICA", FamiliaRecurso = "Caños de acero para instalación eléctrica",                   IndiceNormalizado = null,                                                          CuadroReferencia = "41277 - 21",      IncisoCode = null   },
            new IndiceINDEC { Id = 12, CodigoIndice = "CEMENTO_CAL",               FamiliaRecurso = "Cemento y cal",                                                IndiceNormalizado = null,                                                          CuadroReferencia = "2694",            IncisoCode = null   },
            new IndiceINDEC { Id = 13, CodigoIndice = "CERAMICOS_BALDOSAS",        FamiliaRecurso = "Cerámicos, baldosas y losas",                                  IndiceNormalizado = "(1) 37370",                                                   CuadroReferencia = null,              IncisoCode = "(1)"  },
            new IndiceINDEC { Id = 14, CodigoIndice = "CHAPAS_METALICAS",          FamiliaRecurso = "Chapas metálicas y productos metálicos elaborados",            IndiceNormalizado = null,                                                          CuadroReferencia = "2899 42999-2",    IncisoCode = null   },
            new IndiceINDEC { Id = 15, CodigoIndice = "COMB_LUB",                  FamiliaRecurso = "Combustibles, asfaltos y lubricantes",                         IndiceNormalizado = "k) Asfaltos, combustibles y lubricantes",                     CuadroReferencia = "Cuadro 3.2-23",   IncisoCode = "k"    },
            new IndiceINDEC { Id = 16, CodigoIndice = "ELECTROBOMBA",              FamiliaRecurso = "Electrobombas",                                                IndiceNormalizado = "v) Electrobomba",                                             CuadroReferencia = null,              IncisoCode = "v"    },
            new IndiceINDEC { Id = 17, CodigoIndice = "GASTOS_GENERALES",          FamiliaRecurso = "Gastos generales / subcontratos / servicios auxiliares",       IndiceNormalizado = "p) Gastos generales",                                         CuadroReferencia = "Cuadro 1.4",      IncisoCode = "p"    },
            new IndiceINDEC { Id = 18, CodigoIndice = "HERRAMIENTAS_MANO",         FamiliaRecurso = "Herramientas de mano",                                         IndiceNormalizado = null,                                                          CuadroReferencia = "2893 42921-2",    IncisoCode = null   },
            new IndiceINDEC { Id = 19, CodigoIndice = "HIDROFUGOS",                FamiliaRecurso = "Hidrófugos",                                                   IndiceNormalizado = null,                                                          CuadroReferencia = "2699 37990-1",    IncisoCode = null   },
            new IndiceINDEC { Id = 20, CodigoIndice = "ACERO_HIERRO",              FamiliaRecurso = "Hierros, aceros y perfiles básicos",                           IndiceNormalizado = "(2) 2710 27101",                                              CuadroReferencia = null,              IncisoCode = "(2)"  },
            new IndiceINDEC { Id = 21, CodigoIndice = "HORMIGON",                  FamiliaRecurso = "Hormigón elaborado y premoldeados",                            IndiceNormalizado = "s) Hormigón",                                                 CuadroReferencia = "Cuadro 1.9",      IncisoCode = "s"    },
            new IndiceINDEC { Id = 22, CodigoIndice = "IMPERMEABILIZANTES",        FamiliaRecurso = "Impermeabilizantes químicos",                                  IndiceNormalizado = null,                                                          CuadroReferencia = "2422 35110-5",    IncisoCode = null   },
            new IndiceINDEC { Id = 23, CodigoIndice = "INST_ELECTRICA",            FamiliaRecurso = "Instalación eléctrica, iluminación y cableado",                IndiceNormalizado = "g) Artefactos de iluminación y cableado",                     CuadroReferencia = "Cuadro 1.5",      IncisoCode = "g"    },
            new IndiceINDEC { Id = 24, CodigoIndice = "SANITARIA_INCENDIO",        FamiliaRecurso = "Instalación sanitaria y contra incendio",                      IndiceNormalizado = "r) Artefactos para baño y grifería",                          CuadroReferencia = "Cuadro 1.5",      IncisoCode = "r"    },
            new IndiceINDEC { Id = 25, CodigoIndice = "JABALINA",                  FamiliaRecurso = "Jabalinas y puesta a tierra",                                  IndiceNormalizado = null,                                                          CuadroReferencia = "42999 - 51",      IncisoCode = null   },
            new IndiceINDEC { Id = 26, CodigoIndice = "LADRILLOS",                 FamiliaRecurso = "Ladrillos y mampuestos cerámicos",                             IndiceNormalizado = null,                                                          CuadroReferencia = "2693 37350-1",    IncisoCode = null   },
            new IndiceINDEC { Id = 27, CodigoIndice = "MADERA",                    FamiliaRecurso = "Madera y carpintería de madera",                               IndiceNormalizado = "Var 8.1.2 Carpintería de madera",                             CuadroReferencia = null,              IncisoCode = null   },
            new IndiceINDEC { Id = 28, CodigoIndice = "MANO_OBRA",                 FamiliaRecurso = "Mano de obra",                                                 IndiceNormalizado = "a) Mano de obra",                                             CuadroReferencia = "Cuadro 1.4",      IncisoCode = "a"    },
            new IndiceINDEC { Id = 29, CodigoIndice = "MEMBRANAS_PLASTICOS",       FamiliaRecurso = "Membranas, impermeabilizantes plásticos y geosintéticos",      IndiceNormalizado = "w) Membrana impermeabilizante / Productos de plástico",        CuadroReferencia = "Cuadro 3.2-252",  IncisoCode = "w"    },
            new IndiceINDEC { Id = 30, CodigoIndice = "MOSAICO_GRANITICO",         FamiliaRecurso = "Mosaico / granito / marmolería",                               IndiceNormalizado = null,                                                          CuadroReferencia = "37540 - 11",      IncisoCode = null   },
            new IndiceINDEC { Id = 31, CodigoIndice = "MOTORES_ELECTRICOS_AA",     FamiliaRecurso = "Motores eléctricos y equipos electromecánicos / AA",           IndiceNormalizado = "i) Motores eléctricos y equipos de aire acondicionado",        CuadroReferencia = "Cuadro 3.2-31",   IncisoCode = "i"    },
            new IndiceINDEC { Id = 32, CodigoIndice = "MAQUINAS_HERRAMIENTAS",     FamiliaRecurso = "Máquinas herramientas y accesorios",                           IndiceNormalizado = null,                                                          CuadroReferencia = "2922 29221",      IncisoCode = null   },
            new IndiceINDEC { Id = 33, CodigoIndice = "PIEDRAS_ARENAS_ARCILLAS",   FamiliaRecurso = "Piedras, arenas y arcillas",                                   IndiceNormalizado = "(29) 1410 14101",                                             CuadroReferencia = null,              IncisoCode = "(29)" },
            new IndiceINDEC { Id = 34, CodigoIndice = "PINTURA",                   FamiliaRecurso = "Pinturas y solventes",                                         IndiceNormalizado = null,                                                          CuadroReferencia = "51730 - 1",       IncisoCode = null   },
            new IndiceINDEC { Id = 35, CodigoIndice = "PISOS_REVEST",              FamiliaRecurso = "Pisos y revestimientos",                                       IndiceNormalizado = "c) Pisos y revestimientos",                                   CuadroReferencia = null,              IncisoCode = "c"    },
            new IndiceINDEC { Id = 36, CodigoIndice = "TAPA_CHAPA_CAMARA",         FamiliaRecurso = "Tapas de chapa para cámaras",                                  IndiceNormalizado = null,                                                          CuadroReferencia = "37560 - 21",      IncisoCode = null   },
            new IndiceINDEC { Id = 37, CodigoIndice = "VIDRIOS",                   FamiliaRecurso = "Vidrios, espejos y blindex",                                   IndiceNormalizado = "(21) 2610 26101",                                             CuadroReferencia = null,              IncisoCode = "(21)" },
            new IndiceINDEC { Id = 38, CodigoIndice = "VALVULAS_BRONCE",           FamiliaRecurso = "Válvulas de bronce",                                           IndiceNormalizado = "u) Válvulas de bronce",                                       CuadroReferencia = null,              IncisoCode = "u"    },
            new IndiceINDEC { Id = 39, CodigoIndice = "ZOCALO_GRANITICO",          FamiliaRecurso = "Zócalos graníticos",                                           IndiceNormalizado = null,                                                          CuadroReferencia = "37540 - 21",      IncisoCode = null   },
            // Índice especial — no pertenece al Cuadro 5 INDEC
            new IndiceINDEC { Id = 40, CodigoIndice = "COSTO_FINANCIERO",          FamiliaRecurso = "Costo Financiero – Tasas Activas Banco Nación",                IndiceNormalizado = null,                                                          CuadroReferencia = null,              IncisoCode = null   }
        );

        // ── Valores mensuales ────────────────────────────────────────────────────
        // Los valores de índice son dato operativo, no esquema: se cargan por la
        // pantalla de importación (/indices/importar) o el alta manual, nunca por
        // HasData — cada publicación INDEC como seed obligaba a recompilar y migrar,
        // y el model snapshot había crecido a ~22.000 líneas (auditoría 2026-07-20).
        // Los valores ya cargados viven en la base como datos comunes.

        // ── Obras y ponderaciones ────────────────────────────────────────────────
        // Datos operativos, no seed: viven solo en la base (y sus backups). Los seeds
        // de obras reales que existían acá se retiraron en el re-baseline 2026-08-03,
        // junto con las migraciones históricas que insertaban datos con SQL crudo:
        // una base nueva arranca con el esquema y el catálogo INDEC solamente.
    }
}
