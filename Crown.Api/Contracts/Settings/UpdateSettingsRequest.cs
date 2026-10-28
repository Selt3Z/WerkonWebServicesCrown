namespace Crown.Contracts.Settings;

public sealed class UpdateSettingsRequest
{
    public string? TimeZoneId { get; set; }

    public string? Theme { get; set; }

    public string? CurrencyCode { get; set; }

    public bool? HideArchivedByDefault { get; set; }

    public bool? DispatcherCanHardDelete { get; set; }

    public bool? WarehouseEnabled { get; set; }

    public int? AuditRetentionDays { get; set; }

    public int? ListPageSize { get; set; }
}