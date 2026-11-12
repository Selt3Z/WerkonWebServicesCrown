using Microsoft.AspNetCore.Identity;

namespace Crown.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>В списке исполнителей заявок (кому можно направить).</summary>
    public bool CanBeExecutor { get; set; } = true;

    /// <summary>Может подавать заявки на спецтехнику / транспортные заявки (RequestKind.SpecTech).</summary>
    public bool CanCreateTransportRequests { get; set; }

    /// <summary>Может добавлять/править единицы оборудования. Без флага — только запрос и держать чужое на балансе.</summary>
    public bool CanManageEquipment { get; set; }

    /// <summary>Видит пункт меню и страницу «Транспорт».</summary>
    public bool CanViewTransport { get; set; }

    /// <summary>Видит пункт меню и страницу «Спецтехника».</summary>
    public bool CanViewSpecialVehicles { get; set; }

    /// <summary>Видит пункт меню и страницу «Склад» (qty).</summary>
    public bool CanViewWarehouse { get; set; }

    /// <summary>Видит пункт меню и страницу «Сводка».</summary>
    public bool CanViewSummary { get; set; }

    /// <summary>
    /// Рейтинг бригад: доступ к разделу + фигурант рейтинга
    /// (подразделение = бригада).
    /// </summary>
    public bool CanViewBrigadeRating { get; set; }

    /// <summary>Оператор рейтинга: может добавлять/править/удалять остановки.</summary>
    public bool CanOperateBrigadeRating { get; set; }

    /// <summary>Раздел документации пользователя виден + write в свой раздел.</summary>
    public bool DocumentationEnabled { get; set; } = true;

    public DateTime? LastActivityAtUtc { get; set; }
}
