using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

/// <summary>Сообщить об ошибке — все кроме администратора.</summary>
[Authorize]
public sealed class FeedbackModel(CrownApiClient api) : PageModel
{
    [BindProperty]
    public string? SectionKey { get; set; }

    [BindProperty]
    public string? Body { get; set; }

    [BindProperty]
    public List<IFormFile> Files { get; set; } = [];

    public List<(string Key, string Label)> AvailableSections { get; private set; } = [];

    public string? Error { get; private set; }

    public IActionResult OnGet()
    {
        if (User.IsInRole(AppRoles.Administrator))
        {
            return Redirect("/admin/feedback");
        }

        AvailableSections = BuildSections();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.IsInRole(AppRoles.Administrator))
        {
            return Redirect("/admin/feedback");
        }

        AvailableSections = BuildSections();
        var body = (Body ?? string.Empty).Trim();
        if (body.Length == 0)
        {
            Error = "Укажите текст сообщения.";
            return Page();
        }

        string? sectionLabel = null;
        var key = string.IsNullOrWhiteSpace(SectionKey) ? null : SectionKey.Trim();
        if (key is not null)
        {
            sectionLabel = AvailableSections.FirstOrDefault(x => x.Key == key).Label;
            if (string.IsNullOrEmpty(sectionLabel))
            {
                key = null;
                sectionLabel = null;
            }
        }

        var create = await api.PostAsync<FeedbackDetail>(
            "api/feedback",
            new { Body = body, SectionKey = key, SectionLabel = sectionLabel },
            HttpContext.RequestAborted);
        if (create.IsUnauthorized)
        {
            return Challenge();
        }

        if (!create.Ok || create.Data is null)
        {
            Error = create.Error ?? "Не удалось отправить сообщение.";
            return Page();
        }

        foreach (var file in Files.Where(f => f is { Length: > 0 }))
        {
            var upload = await api.UploadFileAsync<FeedbackAttachmentItem>(
                $"api/feedback/{create.Data.Id}/attachments",
                file,
                "file",
                HttpContext.RequestAborted);
            if (!upload.Ok)
            {
                Error = upload.Error ?? "Сообщение создано, но часть файлов не загрузилась.";
                // Уже создано — уводим на ту же страницу с предупреждением через TempData.
                TempData["Ok"] = "Сообщение отправлено, но не все вложения загрузились.";
                return RedirectToPage();
            }
        }

        TempData["Ok"] = "Сообщение отправлено. Спасибо!";
        return RedirectToPage();
    }

    /// <summary>Те же пункты, что в сайдбаре для текущего пользователя.</summary>
    private List<(string Key, string Label)> BuildSections()
    {
        var isAdmin = User.IsInRole(AppRoles.Administrator);
        var isStaff = isAdmin || User.IsInRole(AppRoles.Dispatcher);
        var canManageEquipment = isStaff
            || User.FindFirst(CrownAuthHandler.CanManageEquipmentClaim)?.Value == "1";
        var canViewTransport = isStaff
            || User.FindFirst(CrownAuthHandler.CanViewTransportClaim)?.Value == "1";
        var canViewSpecialVehicles = isStaff
            || User.FindFirst(CrownAuthHandler.CanViewSpecialVehiclesClaim)?.Value == "1";
        var canViewWarehouse = isStaff
            || User.FindFirst(CrownAuthHandler.CanViewWarehouseClaim)?.Value == "1";
        var canViewSummary = isStaff
            || User.FindFirst(CrownAuthHandler.CanViewSummaryClaim)?.Value == "1";
        var canOperateBrigadeRating = isStaff
            || User.FindFirst(CrownAuthHandler.CanOperateBrigadeRatingClaim)?.Value == "1";
        var canViewBrigadeRating = isStaff
            || canOperateBrigadeRating
            || User.FindFirst(CrownAuthHandler.CanViewBrigadeRatingClaim)?.Value == "1";
        var canViewDocumentation = isStaff
            || User.FindFirst(CrownAuthHandler.DocumentationEnabledClaim)?.Value == "1";

        var items = new List<(string, string)>();
        if (!isAdmin)
        {
            items.Add(("today", "Сегодня"));
            items.Add(("calendar", "Календарь"));
        }

        items.Add(("requests", "Заявки"));
        items.Add(("requests-new", "Добавить заявку"));

        if (canViewTransport)
        {
            items.Add(("transport", "Транспорт"));
        }

        if (canManageEquipment)
        {
            items.Add(("equipment", "Оборудование"));
        }

        if (canViewSpecialVehicles)
        {
            items.Add(("special-vehicles", "Спецтехника"));
        }

        if (canViewWarehouse)
        {
            items.Add(("warehouse", "Склад"));
        }

        if (canViewDocumentation)
        {
            items.Add(("documentation", "Документация"));
        }

        items.Add(("catalog-parts", "Каталог"));
        items.Add(("personnel", "Персонал"));
        items.Add(("timesheet", "Табель"));

        if (canViewSummary)
        {
            items.Add(("summary", "Сводка"));
        }

        if (canViewBrigadeRating)
        {
            items.Add(("brigade-rating", "Рейтинг бригад"));
        }

        if (isStaff)
        {
            items.Add(("history", "Журнал"));
        }

        return items;
    }
}
