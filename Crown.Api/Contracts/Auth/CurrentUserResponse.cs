namespace Crown.Contracts.Auth;

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
    public bool DocumentationEnabled { get; set; }
}
