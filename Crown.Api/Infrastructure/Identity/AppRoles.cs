namespace Crown.Infrastructure.Identity;

public static class AppRoles
{
    public const string Administrator = "Administrator";

    /// <summary>Диспетчер: ведёт заявки, склад и чужие каталоги услуг/узлов.</summary>
    public const string Dispatcher = "Dispatcher";

    /// <summary>Подразделение: подаёт заявки другим подразделениям и исполняет назначенные.</summary>
    public const string Department = "Department";

    /// <summary>Заказчик: внешний/внутренний заказчик работ; права выдаются матрицей прав.</summary>
    public const string Customer = "Customer";

    /// <summary>Бригада: фигурант рейтинга бригад (номер и цех хранятся в ApplicationUser).</summary>
    public const string Brigade = "Brigade";

    /// <summary>Старое имя роли — только для миграций/совместимости чтения.</summary>
    public const string InitiatorLegacy = "Initiator";

    public static readonly string[] All =
    [
        Administrator,
        Dispatcher,
        Department,
        Customer,
        Brigade
    ];

    public static readonly string[] BusinessUsers =
    [
        Administrator,
        Dispatcher,
        Department,
        Customer,
        Brigade
    ];

    /// <summary>
    /// Роли, между которыми админ может переключать пользователя
    /// и которым выдаются права через матрицу «Права подразделений».
    /// </summary>
    public static readonly string[] SwitchableRoles =
    [
        Department,
        Customer,
        Brigade
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

    /// <summary>Department / Customer / Brigade — роли, которыми управляет админ из UI и матрицы прав.</summary>
    public static bool IsSwitchableRole(string? role) =>
        role is Department or Customer or Brigade;
}
