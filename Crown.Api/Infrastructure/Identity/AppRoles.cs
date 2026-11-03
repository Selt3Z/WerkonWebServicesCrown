namespace Crown.Infrastructure.Identity;

public static class AppRoles
{
    public const string Administrator = "Administrator";

    /// <summary>Диспетчер: ведёт заявки, склад и чужие каталоги услуг/узлов.</summary>
    public const string Dispatcher = "Dispatcher";

    /// <summary>Подразделение: подаёт заявки другим подразделениям и исполняет назначенные.</summary>
    public const string Department = "Department";

    /// <summary>Старое имя роли — только для миграций/совместимости чтения.</summary>
    public const string InitiatorLegacy = "Initiator";

    public static readonly string[] All =
    [
        Administrator,
        Dispatcher,
        Department
    ];

    public static readonly string[] BusinessUsers =
    [
        Administrator,
        Dispatcher,
        Department
    ];

    public static readonly string[] CanManageUsers =
    [
        Administrator
    ];

    public static readonly string[] CanManagePartsCatalog =
    [
        Administrator,
        Dispatcher
    ];

    public static readonly string[] CanManageWarehouse =
    [
        Administrator,
        Dispatcher
    ];

    public static readonly string[] CanViewAuditLog =
    [
        Administrator,
        Dispatcher
    ];

    public static readonly string[] CanManageBusinessRecords =
    [
        Administrator,
        Dispatcher
    ];

    public static bool IsDispatcherLikeRole(string? role) =>
        role is Dispatcher or Administrator;
}
