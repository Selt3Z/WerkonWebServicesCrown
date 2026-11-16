using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class BrigadeRatingInspectionsModel(CrownApiClient api) : PageModel
{
    /// <summary>Смотреть проверки — те же права, что у страницы рейтинга.</summary>
    private bool CanAccess =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanViewBrigadeRatingClaim)?.Value == "1"
        || User.FindFirst(CrownAuthHandler.CanOperateBrigadeRatingClaim)?.Value == "1";

    /// <summary>Править/добавлять — staff или оператор рейтинга.</summary>
    public bool CanEnter =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanOperateBrigadeRatingClaim)?.Value == "1";

    public List<BrigadeInspectionListItem> Supervising { get; private set; } = [];

    public List<BrigadeInspectionListItem> Pdk { get; private set; } = [];

    public List<BrigadeInspectionListItem> Pbiot { get; private set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        // Один запрос на все виды; API уже отдаёт SequenceNo по убыванию внутри вида.
        var result = await api.GetAsync<List<BrigadeInspectionListItem>>(
            "api/brigade-rating/inspections",
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error ?? "Не удалось загрузить проверки.";
            return Page();
        }

        Supervising = result.Data.Where(x => x.Kind == BrigadeInspectionKind.Supervising).ToList();
        Pdk = result.Data.Where(x => x.Kind == BrigadeInspectionKind.Pdk).ToList();
        Pbiot = result.Data.Where(x => x.Kind == BrigadeInspectionKind.Pbiot).ToList();
        return Page();
    }

    public static string OutcomeLabel(BrigadeInspectionOutcome? outcome) => outcome switch
    {
        BrigadeInspectionOutcome.Prescription => "Предписание",
        BrigadeInspectionOutcome.Remark => "Замечание",
        BrigadeInspectionOutcome.Stoppage => "Остановка",
        _ => "—"
    };

    /// <summary>Дата устранения: либо дата, либо произвольный текст (например «по факту»).</summary>
    public static string FixDateLabel(BrigadeInspectionListItem item)
    {
        if (item.FixDateIsManual)
        {
            return string.IsNullOrWhiteSpace(item.FixDateManualText) ? "—" : item.FixDateManualText;
        }

        return item.FixDate?.ToString("dd.MM.yyyy") ?? "—";
    }

    public static string Dash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;
}
