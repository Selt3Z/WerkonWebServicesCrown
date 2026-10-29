namespace Crown.Infrastructure.Identity;

public static class AppRoles
{
    public const string Administrator = "Administrator";

    /// <summary>Диспетчер: ведёт заявки, склад и чужие каталоги услуг.</summary>
    public const string Dispatcher = "Dispatcher";

    /// <summary>Инициатор: подаёт заявки другим инициаторам и исполняет назначенные ему.</summary>
    public const string Initiator = "Initiator";

    public static readonly string[] All =
    [
        Administrator,
        Dispatcher,
        Initiator
    ];

    public static readonly string[] BusinessUsers =
    [
        Administrator,
        Dispatcher,
        Initiator
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
