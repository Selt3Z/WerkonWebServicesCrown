namespace Crown.Contracts.DepartmentPermissions;

public sealed class DepartmentPermissionUserResponse
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
    public bool DocumentationEnabled { get; set; }
}
