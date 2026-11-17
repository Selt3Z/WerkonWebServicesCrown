using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

/// <summary>Хаб: три отдельные страницы видов проверок.</summary>
[Authorize]
public sealed class BrigadeRatingInspectionsModel : PageModel
{
    private bool CanAccess =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanViewBrigadeRatingClaim)?.Value == "1"
        || User.FindFirst(CrownAuthHandler.CanOperateBrigadeRatingClaim)?.Value == "1";

    public async Task<IActionResult> OnGetAsync()
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        return Page();
    }

    public static string ListPath(BrigadeInspectionKind kind) => kind switch
    {
        BrigadeInspectionKind.Pdk => "/brigade-rating/inspections/pdk",
        BrigadeInspectionKind.Pbiot => "/brigade-rating/inspections/pbiot",
        _ => "/brigade-rating/inspections/supervising"
    };

    public static string KindLabel(BrigadeInspectionKind kind) => kind switch
    {
        BrigadeInspectionKind.Pdk => "ПДК",
        BrigadeInspectionKind.Pbiot => "ПБиОТ",
        _ => "Супервайзинг"
    };

    public static string OutcomeLabel(BrigadeInspectionOutcome? outcome) => outcome switch
    {
        BrigadeInspectionOutcome.Prescription => "Предписание",
        BrigadeInspectionOutcome.Remark => "Замечание",
        BrigadeInspectionOutcome.Stoppage => "Остановка",
        _ => "—"
    };

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
