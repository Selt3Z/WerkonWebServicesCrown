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
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<RequestAttachment> RequestAttachments => Set<RequestAttachment>();
    public DbSet<ParkingLot> ParkingLots => Set<ParkingLot>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<VehicleTrip> VehicleTrips => Set<VehicleTrip>();
    public DbSet<VehicleFuelRefuel> VehicleFuelRefuels => Set<VehicleFuelRefuel>();
    public DbSet<VehicleRepair> VehicleRepairs => Set<VehicleRepair>();
    public DbSet<DocumentationFolder> DocumentationFolders => Set<DocumentationFolder>();
    public DbSet<DocumentationFile> DocumentationFiles => Set<DocumentationFile>();
    public DbSet<EquipmentAsset> EquipmentAssets => Set<EquipmentAsset>();
    public DbSet<EquipmentMovement> EquipmentMovements => Set<EquipmentMovement>();
    public DbSet<SpecialVehicleUnit> SpecialVehicleUnits => Set<SpecialVehicleUnit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ServiceRequest>(entity =>
        {
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Phone).HasMaxLength(40).IsRequired();
            entity.Property(x => x.InitiatorName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ExecutorName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.TransportType).HasMaxLength(100);
            entity.Property(x => x.Body).HasColumnType("text").IsRequired();
            entity.Property(x => x.ExecutorComment).HasColumnType("text");
            entity.Property(x => x.ComplaintText).HasColumnType("text");
            entity.Property(x => x.ComplaintByName).HasMaxLength(200);
            entity.Property(x => x.SpecOperation).HasMaxLength(200);
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.Kind).HasConversion<int>();
            entity.Property(x => x.Xmin)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.Kind);
            entity.HasIndex(x => x.IsUrgent);
            entity.HasIndex(x => x.SubmittedAt);
            entity.HasIndex(x => x.IsArchived);
            entity.HasQueryFilter(x => !x.IsArchived);
            entity.HasIndex(x => x.InitiatorUserId);
            entity.HasIndex(x => x.ExecutorUserId);
            entity.HasIndex(x => x.EquipmentAssetId);
            entity.HasIndex(x => x.SpecialVehicleUnitId);
            // SetNull: заявка остаётся, имя — снимок текстом.
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.InitiatorUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.ExecutorUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<EquipmentAsset>()
                .WithMany()
                .HasForeignKey(x => x.EquipmentAssetId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<SpecialVehicleUnit>()
                .WithMany()
                .HasForeignKey(x => x.SpecialVehicleUnitId)
                .OnDelete(DeleteBehavior.SetNull);
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
            entity.HasIndex(x => x.OwnerUserId);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Code).HasMaxLength(50);
            entity.Property(x => x.Category).HasMaxLength(100);
            entity.Property(x => x.DefaultUnit).HasMaxLength(20);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.DefaultUnitPrice).HasPrecision(18, 2);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
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
            entity.Property(x => x.CanBeExecutor).HasDefaultValue(true);
            entity.Property(x => x.CanCreateTransportRequests).HasDefaultValue(false);
            entity.Property(x => x.CanManageEquipment).HasDefaultValue(false);
            entity.Property(x => x.CanViewTransport).HasDefaultValue(false);
            entity.Property(x => x.CanViewSpecialVehicles).HasDefaultValue(false);
            entity.Property(x => x.CanViewWarehouse).HasDefaultValue(false);
            entity.Property(x => x.CanViewSummary).HasDefaultValue(false);
            entity.Property(x => x.DocumentationEnabled).HasDefaultValue(true);
        });

        modelBuilder.Entity<DocumentationFolder>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.OwnerUserId);
            entity.HasIndex(x => new { x.OwnerUserId, x.ParentFolderId, x.Name });
            entity.HasOne(x => x.ParentFolder)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentFolderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentationFile>(entity =>
        {
            entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(120);
            entity.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => x.OwnerUserId);
            entity.HasIndex(x => x.FolderId);
            entity.HasOne(x => x.Folder)
                .WithMany(x => x.Files)
                .HasForeignKey(x => x.FolderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ParkingLot>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Address).HasMaxLength(400);
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => x.IsActive);
        });

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.Property(x => x.PlateNumber).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.VehicleType).HasMaxLength(100);
            entity.Property(x => x.Vin).HasMaxLength(32);
            entity.HasIndex(x => x.PlateNumber).IsUnique();
            entity.HasIndex(x => x.IsActive);
            entity.HasIndex(x => x.IsInRepair);
            entity.HasOne(x => x.CurrentParking)
                .WithMany()
                .HasForeignKey(x => x.CurrentParkingId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<VehicleTrip>(entity =>
        {
            entity.Property(x => x.BorrowerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Purpose).HasMaxLength(500);
            entity.Property(x => x.Note).HasMaxLength(1000);
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.UsedHours).HasPrecision(10, 2);
            entity.Property(x => x.FuelStart).HasPrecision(8, 1);
            entity.Property(x => x.FuelEnd).HasPrecision(8, 1);
            entity.HasIndex(x => x.VehicleId);
            entity.HasIndex(x => x.BorrowerUserId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.PlannedStartAt);
            entity.HasIndex(x => x.PlannedEndAt);
            entity.HasOne(x => x.Vehicle)
                .WithMany()
                .HasForeignKey(x => x.VehicleId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.BorrowerUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ParkingLot>()
                .WithMany()
                .HasForeignKey(x => x.ReturnParkingId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<VehicleFuelRefuel>(entity =>
        {
            entity.Property(x => x.FuelType).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.Property(x => x.Liters).HasPrecision(10, 2);
            entity.Property(x => x.PricePerLiter).HasPrecision(12, 2);
            entity.Property(x => x.Cost).HasPrecision(12, 2);
            entity.HasIndex(x => x.VehicleId);
            entity.HasIndex(x => x.RefueledAt);
            entity.HasOne(x => x.Vehicle)
                .WithMany()
                .HasForeignKey(x => x.VehicleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VehicleRepair>(entity =>
        {
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.HasIndex(x => x.VehicleId);
            entity.HasIndex(x => x.StartedAt);
            entity.HasOne(x => x.Vehicle)
                .WithMany()
                .HasForeignKey(x => x.VehicleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EquipmentAsset>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.InventoryNumber).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(100);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.CurrentLocationLabel).HasMaxLength(200);
            entity.Property(x => x.OwnerLabel).HasMaxLength(200);
            entity.Property(x => x.Availability).HasConversion<int>();
            entity.HasIndex(x => x.InventoryNumber).IsUnique();
            entity.HasIndex(x => x.IsArchived);
            entity.HasIndex(x => x.Availability);
            entity.HasIndex(x => x.CurrentLocationUserId);
            entity.HasIndex(x => x.OwnerUserId);
            entity.HasQueryFilter(x => !x.IsArchived);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.CurrentLocationUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EquipmentMovement>(entity =>
        {
            entity.Property(x => x.FromLocationLabel).HasMaxLength(200);
            entity.Property(x => x.ToLocationLabel).HasMaxLength(200);
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.Property(x => x.ByUserDisplayName).HasMaxLength(200);
            entity.HasIndex(x => x.EquipmentAssetId);
            entity.HasIndex(x => x.OccurredAt);
            entity.HasIndex(x => x.ServiceRequestId);
            entity.HasOne(x => x.EquipmentAsset)
                .WithMany(x => x.Movements)
                .HasForeignKey(x => x.EquipmentAssetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SpecialVehicleUnit>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PlateOrNote).HasMaxLength(120);
            entity.Property(x => x.Availability).HasConversion<int>();
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => x.Availability);
            entity.HasIndex(x => x.IsActive);
        });
    }
}
