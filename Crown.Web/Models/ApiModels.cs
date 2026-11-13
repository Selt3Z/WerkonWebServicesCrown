namespace Crown.Web.Models;

// DTO зеркалят контракты Crown.Api (без project reference — Web остаётся тонким UI).

public static class AppRoles
{
    public const string Administrator = "Administrator";
    public const string Dispatcher = "Dispatcher";
    public const string Department = "Department";

    /// <summary>Админ или диспетчер — ведут заявки, оборудование, каталог, журнал.</summary>
    public const string Staff = Administrator + "," + Dispatcher;

    public static readonly string[] All = [Administrator, Dispatcher, Department];

    public static string Display(string? role) => role switch
    {
        Administrator => "Администратор",
        Dispatcher => "Диспетчер",
        Department => "Подразделение",
        "Initiator" => "Подразделение",
        _ => role ?? string.Empty
    };
}

public sealed class PagedResponse<T>
{
    public List<T> Items { get; set; } = [];
    public bool HasMore { get; set; }
}

public sealed class CurrentUserResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public bool CanBeExecutor { get; set; }
    public bool CanCreateTransportRequests { get; set; }
    public bool CanManageEquipment { get; set; }
    public bool CanViewTransport { get; set; }
    public bool CanViewSpecialVehicles { get; set; }
    public bool CanViewWarehouse { get; set; }
    public bool CanViewSummary { get; set; }
    public bool CanViewBrigadeRating { get; set; }
    public bool CanOperateBrigadeRating { get; set; }
    public bool DocumentationEnabled { get; set; }
}

public sealed class LoginRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginFailureResponse
{
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset? LockoutEndUtc { get; set; }
}

public enum ServiceRequestStatus
{
    Submitted = 1,
    InProgress = 2,
    Rejected = 3,
    Returned = 4,
    Completed = 5,
    Cancelled = 6
}

public enum ServiceRequestKind
{
    Other = 0,
    Equipment = 1,
    SpecTech = 2
}

public enum EquipmentAvailability
{
    Free = 0,
    Reserved = 1
}

public enum SpecialVehicleAvailability
{
    Free = 0,
    Reserved = 1,
    InUse = 2
}

public sealed class ServiceRequestListItem
{
    public long Id { get; set; }
    public ServiceRequestStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public ServiceRequestKind Kind { get; set; }
    public string KindLabel { get; set; } = string.Empty;
    public bool IsUrgent { get; set; }
    public bool Acknowledged { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string InitiatorName { get; set; } = string.Empty;
    public Guid? InitiatorUserId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? ReactionAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string ExecutorName { get; set; } = string.Empty;
    public Guid? ExecutorUserId { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string BodyPreview { get; set; } = string.Empty;
    public DateTimeOffset DueAt { get; set; }
    public bool HasComplaint { get; set; }
    public bool IsArchived { get; set; }
}

public sealed class ServiceRequestDetail
{
    public long Id { get; set; }
    public ServiceRequestStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public ServiceRequestKind Kind { get; set; }
    public string KindLabel { get; set; } = string.Empty;
    public bool IsUrgent { get; set; }
    public bool Acknowledged { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public string Phone { get; set; } = string.Empty;
    public DateTimeOffset DueAt { get; set; }
    public string InitiatorName { get; set; } = string.Empty;
    public Guid? InitiatorUserId { get; set; }
    public string ExecutorName { get; set; } = string.Empty;
    public Guid? ExecutorUserId { get; set; }
    public string? TransportType { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? ExecutorComment { get; set; }
    public DateTimeOffset? ReactionAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? RequestedEquipmentCategory { get; set; }
    public Guid? EquipmentAssetId { get; set; }
    public string? EquipmentAssetLabel { get; set; }
    public string? RequestedSpecialVehicleType { get; set; }
    public Guid? SpecialVehicleUnitId { get; set; }
    public string? SpecialVehicleLabel { get; set; }
    public int QueueAheadCount { get; set; }
    public string? SpecOperation { get; set; }
    public DateTimeOffset? SpecPlannedAt { get; set; }
    public DateTimeOffset? SpecArrivedAt { get; set; }
    public DateTimeOffset? SpecReleasedAt { get; set; }
    public string? RowVersion { get; set; }
    public bool HasComplaint { get; set; }
    public string? ComplaintText { get; set; }
    public DateTimeOffset? ComplaintAt { get; set; }
    public string? ComplaintByName { get; set; }
    public bool IsArchived { get; set; }
    public List<RequestAttachment> Attachments { get; set; } = [];
}

public sealed class RequestAssignee
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class TodayRequestsResponse
{
    public DateOnly Date { get; set; }
    public List<ServiceRequestListItem> Incoming { get; set; } = [];
    public List<ServiceRequestListItem> Outgoing { get; set; } = [];
}

public sealed class FileComplaintRequest
{
    public string Text { get; set; } = string.Empty;
    public bool Confirmed { get; set; }
}

public sealed class RequestAttachment
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

public sealed class AttachmentPreviewResponse
{
    public string Kind { get; set; } = "unsupported";
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string? Text { get; set; }
    public List<AttachmentPreviewSheet> Sheets { get; set; } = [];
    public List<AttachmentArchiveEntry> Entries { get; set; } = [];
    public string? Message { get; set; }
}

public sealed class AttachmentPreviewSheet
{
    public string Name { get; set; } = string.Empty;
    public List<List<string>> Rows { get; set; } = [];
}

public sealed class AttachmentArchiveEntry
{
    public string Path { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long? SizeBytes { get; set; }
}

public sealed class CreateServiceRequestRequest
{
    public string Phone { get; set; } = string.Empty;
    public DateTimeOffset DueAt { get; set; }
    public Guid? InitiatorUserId { get; set; }
    public Guid ExecutorUserId { get; set; }
    public string? TransportType { get; set; }
    public string Body { get; set; } = string.Empty;
    public ServiceRequestKind Kind { get; set; } = ServiceRequestKind.Other;
    public bool IsUrgent { get; set; }
    public string? RequestedEquipmentCategory { get; set; }
    public string? RequestedSpecialVehicleType { get; set; }
    public string? SpecOperation { get; set; }
    public DateTimeOffset? SpecPlannedAt { get; set; }
}

public sealed class TransitionServiceRequestRequest
{
    public string Action { get; set; } = string.Empty;
    public string? ExecutorComment { get; set; }
    /// <summary>Текст заявки — повторная подача после возврата.</summary>
    public string? Body { get; set; }
    public DateTimeOffset? NewDueAt { get; set; }
    public string? RowVersion { get; set; }
    public bool? Acknowledged { get; set; }
    public Guid? EquipmentAssetId { get; set; }
    public Guid? SpecialVehicleUnitId { get; set; }
}

public sealed class ReassignServiceRequestRequest
{
    public Guid ExecutorUserId { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string? RowVersion { get; set; }
}

public class EquipmentAsset
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string InventoryNumber { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Description { get; set; }
    public Guid? CurrentLocationUserId { get; set; }
    public string? CurrentLocationLabel { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string? OwnerLabel { get; set; }
    public EquipmentAvailability Availability { get; set; }
    public long? ReservedByRequestId { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class EquipmentMovement
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? FromLocationUserId { get; set; }
    public string? FromLocationLabel { get; set; }
    public Guid? ToLocationUserId { get; set; }
    public string? ToLocationLabel { get; set; }
    public long? ServiceRequestId { get; set; }
    public string? Note { get; set; }
    public string? ByUserDisplayName { get; set; }
}

public sealed class EquipmentAssetDetail : EquipmentAsset
{
    public List<EquipmentMovement> Movements { get; set; } = [];
}

public sealed class EquipmentRequestCategory
{
    public string Name { get; set; } = string.Empty;
    public int FreeCount { get; set; }
    public int TotalActive { get; set; }
}

public sealed class SaveEquipmentAssetRequest
{
    public string Name { get; set; } = string.Empty;
    public string InventoryNumber { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Description { get; set; }
    public Guid? CurrentLocationUserId { get; set; }
    public string? CurrentLocationLabel { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string? OwnerLabel { get; set; }
}

public sealed class EquipmentLocationOption
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class SpecialVehicleUnit
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PlateOrNote { get; set; }
    public SpecialVehicleAvailability Availability { get; set; }
    public long? ReservedByRequestId { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class SaveSpecialVehicleUnitRequest
{
    public string Name { get; set; } = string.Empty;
    public string? PlateOrNote { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class SpecialVehicleAvailabilityPayload
{
    public List<SpecialVehicleTypeAvailability> Types { get; set; } = [];
    public List<SpecialVehicleUnit> FreeUnits { get; set; } = [];
}

public sealed class SpecialVehicleTypeAvailability
{
    public string Name { get; set; } = string.Empty;
    public int FreeCount { get; set; }
    public int TotalActive { get; set; }
}

public sealed class WarehousePart
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Barcode { get; set; }
    public string? Category { get; set; }
    public decimal DefaultUnitPrice { get; set; }
    public string DefaultUnit { get; set; } = string.Empty;
    public decimal QuantityOnHand { get; set; }
    public decimal MinQuantityOnHand { get; set; }
    public string? Description { get; set; }
    public bool IsArchived { get; set; }
}

public sealed class SaveWarehousePartRequest
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string? Category { get; set; }
    public decimal DefaultUnitPrice { get; set; }
    public string DefaultUnit { get; set; } = "шт";
    public decimal QuantityOnHand { get; set; }
    public decimal MinQuantityOnHand { get; set; } = 1m;
    public string? Description { get; set; }
}

public sealed class CatalogPart
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string OwnerDisplayName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Category { get; set; }
    public decimal DefaultUnitPrice { get; set; }
    public string DefaultUnit { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? SlaHours { get; set; }
    public bool IsActive { get; set; }
}

public sealed class SaveCatalogPartRequest
{
    public Guid? OwnerUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Category { get; set; }
    public decimal DefaultUnitPrice { get; set; }
    public string DefaultUnit { get; set; } = "шт";
    public string? Description { get; set; }
    public int? SlaHours { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Единицы измерения — зеркало API MeasurementUnits.All.</summary>
public static class MeasurementUnitOptions
{
    public static readonly string[] All = ["шт", "л", "кг", "м", "компл", "уп"];
}

public sealed class AuditLogItem
{
    public Guid Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string UserDisplayName { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? EntityUrl { get; set; }
}

public sealed class AuditLogUserOption
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class UserListItem
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? LastActivityAtUtc { get; set; }
}

public sealed class DocumentationSection
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool CanWrite { get; set; }
    public int FileCount { get; set; }
    public long TotalSizeBytes { get; set; }
}

public sealed class DocumentationGlobalSearch
{
    public string Query { get; set; } = string.Empty;
    public List<DocumentationGlobalSearchHit> Hits { get; set; } = [];
}

public sealed class DocumentationGlobalSearchHit
{
    public Guid OwnerUserId { get; set; }
    public string OwnerDisplayName { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PathLabel { get; set; }
    public Guid? FolderId { get; set; }
    public long? SizeBytes { get; set; }
    public DateTimeOffset? At { get; set; }
}

public sealed class DocumentationFolderItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentFolderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? PathLabel { get; set; }
}

public sealed class DocumentationFileItem
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public Guid? FolderId { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public string? PathLabel { get; set; }
}

public sealed class DocumentationBrowse
{
    public Guid OwnerUserId { get; set; }
    public string OwnerDisplayName { get; set; } = string.Empty;
    public Guid? FolderId { get; set; }
    public string? FolderName { get; set; }
    public Guid? ParentFolderId { get; set; }
    public bool CanWrite { get; set; }
    public bool DeletesPermanently { get; set; }
    public bool CanViewTrash { get; set; }
    public bool CanClearTrash { get; set; }
    public string? SearchQuery { get; set; }
    public List<DocumentationFolderItem> Folders { get; set; } = [];
    public List<DocumentationFileItem> Files { get; set; } = [];
}

public sealed class DocumentationTrashItem
{
    public Guid Id { get; set; }
    public bool IsFolder { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? SizeBytes { get; set; }
    public DateTimeOffset DeletedAt { get; set; }
    public string? PathLabel { get; set; }
}

public sealed class DocumentationTrash
{
    public Guid OwnerUserId { get; set; }
    public string OwnerDisplayName { get; set; } = string.Empty;
    public bool CanClearTrash { get; set; }
    public int RetentionDays { get; set; }
    public List<DocumentationTrashItem> Items { get; set; } = [];
}

public sealed class DocumentationAdminUser
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool DocumentationEnabled { get; set; }
}

public sealed class UpdateDocumentationSettingsRequest
{
    public List<DocumentationAdminUserToggle> Users { get; set; } = [];
}

public sealed class DocumentationAdminUserToggle
{
    public Guid UserId { get; set; }
    public bool DocumentationEnabled { get; set; }
}

public sealed class DepartmentPermissionUser
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool CanBeExecutor { get; set; }
    public bool CanCreateTransportRequests { get; set; }
    public bool CanManageEquipment { get; set; }
    public bool CanViewTransport { get; set; }
    public bool CanViewSpecialVehicles { get; set; }
    public bool CanViewWarehouse { get; set; }
    public bool CanViewSummary { get; set; }
    public bool CanViewBrigadeRating { get; set; }
    public bool CanOperateBrigadeRating { get; set; }
    public bool DocumentationEnabled { get; set; }
}

public sealed class UpdateDepartmentPermissionsRequest
{
    public List<DepartmentPermissionUserToggle> Users { get; set; } = [];
}

public sealed class DepartmentPermissionUserToggle
{
    public Guid UserId { get; set; }
    public bool CanBeExecutor { get; set; }
    public bool CanCreateTransportRequests { get; set; }
    public bool CanManageEquipment { get; set; }
    public bool CanViewTransport { get; set; }
    public bool CanViewSpecialVehicles { get; set; }
    public bool CanViewWarehouse { get; set; }
    public bool CanViewSummary { get; set; }
    public bool CanViewBrigadeRating { get; set; }
    public bool CanOperateBrigadeRating { get; set; }
    public bool DocumentationEnabled { get; set; }
}

public sealed class BrigadeParticipant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}

public sealed class StoppageActItem
{
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

public sealed class SaveStoppageActItemRequest
{
    public string Text { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class StoppageWorkItem
{
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

public sealed class SaveStoppageWorkItemRequest
{
    public string Text { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class PersonnelPerson
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string OwnerDisplayName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Position { get; set; }
    public bool IsActive { get; set; }
}

public sealed class SavePersonnelPersonRequest
{
    public Guid? OwnerUserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Position { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class StoppageRemarkItem
{
    public Guid Id { get; set; }
    public Guid? ActItemId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public bool IsManual { get; set; }
    public bool IsResolved { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public int SortOrder { get; set; }
}

public class BrigadeStoppageListItem
{
    public Guid Id { get; set; }
    public DateOnly ReportDate { get; set; }
    public Guid BrigadeUserId { get; set; }
    public string BrigadeName { get; set; } = string.Empty;
    public string MasterName { get; set; } = string.Empty;
    public Guid? MasterPersonId { get; set; }
    public string StoppedByName { get; set; } = string.Empty;
    public Guid? StoppedByUserId { get; set; }
    public Guid? StoppedByPersonId { get; set; }
    public TimeOnly StoppedAt { get; set; }
    public TimeOnly? RestartedAt { get; set; }
    public decimal Hours { get; set; }
    public string WorkDescription { get; set; } = string.Empty;
    public Guid? WorkItemId { get; set; }
    public bool IsWorkManual { get; set; } = true;
    public int RemarkCount { get; set; }
    public int UnresolvedRemarkCount { get; set; }
}

public sealed class BrigadeStoppageDetail : BrigadeStoppageListItem
{
    public List<StoppageRemarkItem> Remarks { get; set; } = [];
}

public sealed class SaveStoppageRemarkRequest
{
    public Guid? Id { get; set; }
    public Guid? ActItemId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public bool IsManual { get; set; }
    public bool IsResolved { get; set; }
}

public sealed class SaveBrigadeStoppageRequest
{
    public Guid BrigadeUserId { get; set; }
    public DateOnly ReportDate { get; set; }
    public string? MasterName { get; set; }
    public Guid? MasterPersonId { get; set; }
    public Guid? StoppedByUserId { get; set; }
    public Guid? StoppedByPersonId { get; set; }
    public string? StoppedByName { get; set; }
    public TimeOnly StoppedAt { get; set; }
    public TimeOnly? RestartedAt { get; set; }
    public string WorkDescription { get; set; } = string.Empty;
    public Guid? WorkItemId { get; set; }
    public bool IsWorkManual { get; set; } = true;
    public List<SaveStoppageRemarkRequest> Remarks { get; set; } = [];
}

public sealed class SetRemarkResolvedRequest
{
    public bool IsResolved { get; set; }
}

public sealed class BrigadeRatingDashboard
{
    public string Period { get; set; } = "month";
    public int EarliestYear { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public int StoppageCount { get; set; }
    public int RemarkCount { get; set; }
    public int ResolvedRemarkCount { get; set; }
    public int UnresolvedRemarkCount { get; set; }
    public decimal TotalHours { get; set; }
    public decimal ResolutionRatePercent { get; set; }
    public decimal AverageHours { get; set; }
    public BrigadeRatingPeriodComparison PeriodComparison { get; set; } = new();
    public List<BrigadeDailyTrendPoint> DailyTrend { get; set; } = [];
    public List<StopperTopItem> TopStoppers { get; set; } = [];
    public List<BrigadeRankItem> BrigadeRanking { get; set; } = [];
    public List<RemarkTopItem> TopRemarks { get; set; } = [];
    public List<WorkTopItem> TopWorks { get; set; } = [];
    public List<BrigadeStoppageListItem> Stoppages { get; set; } = [];
}

public sealed class BrigadeRatingPeriodComparison
{
    public DateOnly PreviousPeriodStart { get; set; }
    public DateOnly PreviousPeriodEnd { get; set; }
    public int PreviousStoppageCount { get; set; }
    public int PreviousRemarkCount { get; set; }
    public int PreviousResolvedRemarkCount { get; set; }
    public int PreviousUnresolvedRemarkCount { get; set; }
    public decimal PreviousTotalHours { get; set; }
    public decimal PreviousResolutionRatePercent { get; set; }
    public decimal PreviousAverageHours { get; set; }
}

public sealed class BrigadeDailyTrendPoint
{
    public DateOnly Date { get; set; }
    public string Label { get; set; } = string.Empty;
    public int StoppageCount { get; set; }
    public decimal Hours { get; set; }
    public int RemarkCount { get; set; }
    public int UnresolvedRemarkCount { get; set; }
}

public sealed class StopperTopItem
{
    public string Name { get; set; } = string.Empty;
    public int StoppageCount { get; set; }
    public decimal TotalHours { get; set; }
}

public sealed class BrigadeRankItem
{
    public Guid BrigadeUserId { get; set; }
    public string BrigadeName { get; set; } = string.Empty;
    public string MasterName { get; set; } = string.Empty;
    public int StoppageCount { get; set; }
    public int RemarkCount { get; set; }
    public int UnresolvedRemarkCount { get; set; }
    public decimal TotalHours { get; set; }
    public decimal Score { get; set; }
}

public sealed class RemarkTopItem
{
    public string Text { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class WorkTopItem
{
    public string Text { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class SpreadsheetImportPreview
{
    public List<string> Headers { get; set; } = [];
    public List<List<string>> SampleRows { get; set; } = [];
    public int TotalRows { get; set; }
}

public sealed class SpreadsheetImportCommitRequest
{
    public string FileBase64 { get; set; } = string.Empty;
    public int? NameColumn { get; set; }
    public int? InventoryNumberColumn { get; set; }
    public int? CategoryColumn { get; set; }
    public int? DescriptionColumn { get; set; }
    public int? LocationColumn { get; set; }
}

public sealed class SpreadsheetImportCommitResult
{
    public int Created { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public List<SpreadsheetImportRowError> ErrorRows { get; set; } = [];
}

public sealed class SpreadsheetImportRowError
{
    public int RowNumber { get; set; }
    public string Code { get; set; } = string.Empty;
}

public sealed class SaveUserRequest
{
    public string UserName { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? CurrentPassword { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class SettingsResponse
{
    public string TimeZoneId { get; set; } = string.Empty;
    public string Theme { get; set; } = "light";
    public string CurrencyCode { get; set; } = "RUB";
    public int CurrencyDecimalPlaces { get; set; } = 2;
    public bool HideArchivedByDefault { get; set; } = true;
    public bool DispatcherCanHardDelete { get; set; }
    public bool WarehouseEnabled { get; set; }
    public int AuditRetentionDays { get; set; } = 30;
    public int DocumentationTrashRetentionDays { get; set; } = 7;
    public int ListPageSize { get; set; } = 30;
    public string BrandLogoMode { get; set; } = "standard";
    public bool HasCustomLogo { get; set; }
    public string? LogoDataUrl { get; set; }
    public string AttachmentsHostPath { get; set; } = string.Empty;
    public string AttachmentsContainerPath { get; set; } = "/data/attachments";
    public string? AttachmentsLegacyHostPath { get; set; }
    public bool AttachmentsLegacyActive { get; set; }
}

public sealed class UpdateSettingsRequest
{
    public string? TimeZoneId { get; set; }
    public string? Theme { get; set; }
    public string? CurrencyCode { get; set; }
    public bool? HideArchivedByDefault { get; set; }
    public bool? DispatcherCanHardDelete { get; set; }
    public bool? WarehouseEnabled { get; set; }
    public int? AuditRetentionDays { get; set; }
    public int? DocumentationTrashRetentionDays { get; set; }
    public int? ListPageSize { get; set; }
    public string? BrandLogoMode { get; set; }
}

public sealed class UploadOrganizationLogoRequest
{
    public string ContentType { get; set; } = string.Empty;
    public string DataBase64 { get; set; } = string.Empty;
}

public sealed class OrganizationProfileResponse
{
    public bool HasLogo { get; set; }
    public string? LogoDataUrl { get; set; }
}

public sealed class UpdateTimeZoneRequest
{
    public string TimeZoneId { get; set; } = string.Empty;
}

public sealed class BackupStatus
{
    public bool IsConfigured { get; set; }
    public string? LastRunUtc { get; set; }
    public string? LastStatus { get; set; }
    public string? LastMessage { get; set; }
    public long? LastBackupSizeBytes { get; set; }
    public bool ResticEnabled { get; set; }
    public bool BackupOkToday { get; set; }
    public bool IsRunning { get; set; }
    public int ProgressPercent { get; set; }
    public int IntervalSeconds { get; set; }
    public string? DailyAt { get; set; }
    public string? TimeZoneId { get; set; }
    public int DailyRetentionDays { get; set; }
    public int WeeklyRetentionWeeks { get; set; }
}

public sealed class BackupFileInfo
{
    public string RelativePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string CreatedUtc { get; set; } = string.Empty;
}

public sealed class DiskVolume
{
    public string Name { get; set; } = string.Empty;
    public string Mount { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long UsedBytes { get; set; }
    public long FreeBytes { get; set; }
    public int UsedPercent { get; set; }
}

public sealed class ComposeServiceStatus
{
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Health { get; set; }
}

public sealed class SystemHealthDetails
{
    public bool DatabaseHealthy { get; set; }
    public bool DatabaseSchemaUpToDate { get; set; }
    public int PendingMigrationCount { get; set; }
    public List<string> PendingMigrations { get; set; } = [];
    public string? LastAppliedMigration { get; set; }
    public bool BackupOkToday { get; set; }
    public string? BackupLastStatus { get; set; }
    public string? BackupLastRunUtc { get; set; }
    public List<DiskVolume> Disks { get; set; } = [];
    public List<ComposeServiceStatus> Containers { get; set; } = [];
}

public sealed class SystemStatusResponse
{
    public string ApplicationVersion { get; set; } = string.Empty;
    public bool DatabaseHealthy { get; set; }
    public bool DatabaseSchemaUpToDate { get; set; }
    public int PendingMigrationCount { get; set; }
    public string? LastAppliedMigration { get; set; }
    public string ServerTimeUtc { get; set; } = string.Empty;
    public string AppTimeZoneId { get; set; } = string.Empty;
    public string ServerLocalTime { get; set; } = string.Empty;
    public int AuditLogEntryCount { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string PublicHostname { get; set; } = string.Empty;
    public List<string> AdvertiseAddresses { get; set; } = [];
    public List<string> InterfaceAddresses { get; set; } = [];
    public BackupStatus Backup { get; set; } = new();
    public List<BackupFileInfo> AvailableBackups { get; set; } = [];
    public SystemHealthDetails Health { get; set; } = new();
}

public sealed class ManualBackupResponse
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}

public sealed class HostPowerResponse
{
    public bool Started { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}

public sealed class SupportContactResponse
{
    public bool Sent { get; set; }
    public bool WebhookConfigured { get; set; }
    public string ReportText { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}

public sealed class ResetSessionsResponse
{
    public bool Success { get; set; }
    public int UsersAffected { get; set; }
    public string? Message { get; set; }
}

public sealed class SummaryResponse
{
    public string Period { get; set; } = "today";
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public Guid UserId { get; set; }
    public string UserDisplayName { get; set; } = string.Empty;
    public List<SummaryUserOption>? AvailableUsers { get; set; }
    public int OutgoingCount { get; set; }
    public int IncomingCount { get; set; }
    public int TotalCount { get; set; }
    public int CompletedOutgoingCount { get; set; }
    public int CompletedIncomingCount { get; set; }
    public int CompletedTotalCount { get; set; }
    public decimal CompletionRatePercent { get; set; }
    public decimal OutgoingSharePercent { get; set; }
    public decimal IncomingSharePercent { get; set; }
    public decimal? AverageReactionHours { get; set; }
    public int OpenOutgoingCount { get; set; }
    public int OpenIncomingCount { get; set; }
    public int OverdueOutgoingCount { get; set; }
    public int OverdueIncomingCount { get; set; }
    public int WaitingAcceptIncomingCount { get; set; }
    public int UnacknowledgedIncomingCount { get; set; }
    public int ReturnedOutgoingCount { get; set; }
    public int UrgentPeriodCount { get; set; }
    public int OpenUrgentCount { get; set; }
    public int ComplaintsPeriodCount { get; set; }
    public decimal? AverageCompletionHours { get; set; }
    public SummaryStatusBreakdown OutgoingStatusBreakdown { get; set; } = new();
    public SummaryStatusBreakdown IncomingStatusBreakdown { get; set; } = new();
    public SummaryKindBreakdown KindBreakdown { get; set; } = new();
    public SummaryPeriodComparison PeriodComparison { get; set; } = new();
    public List<SummaryDailyTrendItem> DailyTrend { get; set; } = [];
}

public sealed class SummaryUserOption
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class SummaryStatusBreakdown
{
    public int SubmittedCount { get; set; }
    public int InProgressCount { get; set; }
    public int ReturnedCount { get; set; }
    public int RejectedCount { get; set; }
    public int CompletedCount { get; set; }
    public int CancelledCount { get; set; }
}

public sealed class SummaryKindBreakdown
{
    public int OtherCount { get; set; }
    public int EquipmentCount { get; set; }
    public int SpecTechCount { get; set; }
}

public sealed class SummaryPeriodComparison
{
    public DateOnly PreviousPeriodStart { get; set; }
    public DateOnly PreviousPeriodEnd { get; set; }
    public int PreviousOutgoingCount { get; set; }
    public int PreviousIncomingCount { get; set; }
    public int PreviousTotalCount { get; set; }
    public int PreviousCompletedTotalCount { get; set; }
    public decimal PreviousCompletionRatePercent { get; set; }
}

public sealed class SummaryDailyTrendItem
{
    public DateOnly Date { get; set; }
    public string Label { get; set; } = string.Empty;
    public int SubmittedCount { get; set; }
    public int CompletedCount { get; set; }
}

public enum VehicleTripStatus
{
    Reserved = 1,
    Active = 2,
    Returned = 3,
    Cancelled = 4
}

public sealed class ParkingLotItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; }
}

public sealed class TransportVehicleItem
{
    public Guid Id { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? VehicleType { get; set; }
    public string? Vin { get; set; }
    public bool IsInRepair { get; set; }
    public Guid? CurrentParkingId { get; set; }
    public string? CurrentParkingName { get; set; }
    public bool IsActive { get; set; }
    public string Availability { get; set; } = "free";
    public Guid? ActiveTripId { get; set; }
    public string? ActiveBorrowerName { get; set; }
    public DateTimeOffset? ActivePlannedEndAt { get; set; }
    public bool IsOverdue { get; set; }
}

public sealed class VehicleFuelRefuelItem
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public DateTimeOffset RefueledAt { get; set; }
    public decimal Liters { get; set; }
    public string FuelType { get; set; } = string.Empty;
    public decimal PricePerLiter { get; set; }
    public decimal Cost { get; set; }
    public string? Note { get; set; }
}

public sealed class VehicleRepairItem
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public decimal? DurationHours { get; set; }
    public bool IsOpen { get; set; }
    public string? Note { get; set; }
}

public sealed class VehicleDetailResponse
{
    public TransportVehicleItem Vehicle { get; set; } = new();
    public List<VehicleFuelRefuelItem> Refuels { get; set; } = [];
    public List<VehicleTripItem> Trips { get; set; } = [];
    public List<VehicleRepairItem> Repairs { get; set; } = [];
    public decimal TotalLiters { get; set; }
    public decimal TotalCost { get; set; }
}

public sealed class SaveVehicleFuelRefuelRequest
{
    public DateTimeOffset? RefueledAt { get; set; }
    public decimal Liters { get; set; }
    public string FuelType { get; set; } = string.Empty;
    public decimal PricePerLiter { get; set; }
    public string? Note { get; set; }
}

public sealed class VehicleTripItem
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public string VehicleName { get; set; } = string.Empty;
    public Guid BorrowerUserId { get; set; }
    public string BorrowerName { get; set; } = string.Empty;
    public VehicleTripStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public DateTimeOffset PlannedStartAt { get; set; }
    public DateTimeOffset PlannedEndAt { get; set; }
    public DateTimeOffset? ActualStartAt { get; set; }
    public DateTimeOffset? ActualEndAt { get; set; }
    public decimal? UsedHours { get; set; }
    public int? LateMinutes { get; set; }
    public string? Purpose { get; set; }
    public int? OdometerStart { get; set; }
    public int? OdometerEnd { get; set; }
    public decimal? FuelStart { get; set; }
    public decimal? FuelEnd { get; set; }
    public Guid? ReturnParkingId { get; set; }
    public string? ReturnParkingName { get; set; }
    public string? Note { get; set; }
    public bool IsOverdue { get; set; }
}

public sealed class TransportBorrowerOption
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class TransportBoardResponse
{
    public List<TransportVehicleItem> Free { get; set; } = [];
    public List<TransportVehicleItem> Busy { get; set; } = [];
    public List<ParkingLotItem> Parkings { get; set; } = [];
    public List<VehicleTripItem> RecentReturns { get; set; } = [];
    public List<TransportBorrowerOption> Borrowers { get; set; } = [];
}

public sealed class SaveParkingLotRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class SaveVehicleRequest
{
    public string PlateNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? VehicleType { get; set; }
    public string? Vin { get; set; }
    public bool IsInRepair { get; set; }
    public Guid? CurrentParkingId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CreateVehicleTripRequest
{
    public Guid VehicleId { get; set; }
    public Guid BorrowerUserId { get; set; }
    public DateTimeOffset? PlannedStartAt { get; set; }
    public DateTimeOffset PlannedEndAt { get; set; }
    public string? Purpose { get; set; }
    public int? OdometerStart { get; set; }
    public decimal? FuelStart { get; set; }
    public string? Note { get; set; }
}

public sealed class StartVehicleTripRequest
{
    public int? OdometerStart { get; set; }
    public decimal? FuelStart { get; set; }
}

public sealed class ReturnVehicleTripRequest
{
    public Guid? ReturnParkingId { get; set; }
    public int? OdometerEnd { get; set; }
    public decimal? FuelEnd { get; set; }
    public string? Note { get; set; }
}
