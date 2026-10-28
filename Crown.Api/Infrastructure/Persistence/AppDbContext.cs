using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Crown.Domain.Entities;
using Crown.Infrastructure.Identity;

namespace Crown.Infrastructure.Persistence;

public sealed class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<CatalogPart> CatalogParts => Set<CatalogPart>();
    public DbSet<WarehousePart> WarehouseParts => Set<WarehousePart>();
    public DbSet<WarehouseStockMovement> WarehouseStockMovements => Set<WarehouseStockMovement>();
    public DbSet<ServiceDepartment> ServiceDepartments => Set<ServiceDepartment>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<RequestAttachment> RequestAttachments => Set<RequestAttachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ServiceDepartment>(entity =>
        {
            entity.HasIndex(x => x.Name).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<ServiceRequest>(entity =>
        {
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Phone).HasMaxLength(40).IsRequired();
            entity.Property(x => x.InitiatorName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.TransportType).HasMaxLength(100);
            entity.Property(x => x.Body).HasColumnType("text").IsRequired();
            entity.Property(x => x.ExecutorComment).HasColumnType("text");
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.Xmin)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.SubmittedAt);
            entity.HasIndex(x => x.IsArchived);
            entity.HasQueryFilter(x => !x.IsArchived);
            entity.HasOne(x => x.ServiceDepartment)
                .WithMany()
                .HasForeignKey(x => x.ServiceDepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RequestAttachment>(entity =>
        {
            entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(120);
            entity.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => x.ServiceRequestId);
            entity.HasOne(x => x.ServiceRequest)
                .WithMany(x => x.Attachments)
                .HasForeignKey(x => x.ServiceRequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(128);
            entity.Property(x => x.Value).HasColumnType("text");
        });

        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.HasIndex(x => x.OccurredAtUtc);
            entity.Property(x => x.UserDisplayName).HasMaxLength(200);
            entity.Property(x => x.Action).HasMaxLength(32);
            entity.Property(x => x.EntityType).HasMaxLength(64);
            entity.Property(x => x.Summary).HasMaxLength(500);
            entity.Property(x => x.EntityUrl).HasMaxLength(256);
        });

        modelBuilder.Entity<CatalogPart>(entity =>
        {
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => x.Code);
            entity.HasIndex(x => x.Category);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Code).HasMaxLength(50);
            entity.Property(x => x.Category).HasMaxLength(100);
            entity.Property(x => x.DefaultUnit).HasMaxLength(20);
            entity.Property(x => x.Description).HasMaxLength(1000);
        });

        modelBuilder.Entity<WarehousePart>(entity =>
        {
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => x.Code);
            entity.HasIndex(x => x.Barcode);
            entity.HasIndex(x => x.Category);
            entity.HasIndex(x => x.IsArchived);
            entity.HasQueryFilter(x => !x.IsArchived);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Code).HasMaxLength(50);
            entity.Property(x => x.Barcode).HasMaxLength(100);
            entity.Property(x => x.Category).HasMaxLength(100);
            entity.Property(x => x.DefaultUnit).HasMaxLength(20);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.QuantityOnHand).HasPrecision(18, 1);
            entity.Property(x => x.MinQuantityOnHand).HasPrecision(18, 1).HasDefaultValue(1m);
            entity.Property(x => x.DefaultUnitPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<WarehouseStockMovement>(entity =>
        {
            entity.ToTable("WarehouseStockMovements");
            entity.HasIndex(x => x.WarehousePartId);
            entity.HasIndex(x => x.OccurredAtUtc);
            entity.HasIndex(x => x.ServiceRequestId);
            entity.Property(x => x.QuantityDelta).HasPrecision(18, 1);
            entity.Property(x => x.QuantityAfter).HasPrecision(18, 1);
            entity.Property(x => x.Kind).HasConversion<int>();
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.Property(x => x.UserDisplayName).HasMaxLength(200);

            entity.HasOne(x => x.WarehousePart)
                .WithMany(x => x.StockMovements)
                .HasForeignKey(x => x.WarehousePartId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(200);
        });
    }
}
