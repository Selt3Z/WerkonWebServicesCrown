using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages.Requests;

public sealed class NewModel(CrownApiClient api, AppTime time) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public List<IFormFile> Files { get; set; } = [];

    public List<ServiceDepartment> Departments { get; private set; } = [];

    public string? Error { get; private set; }

    public sealed class InputModel
    {
        public string? Phone { get; set; }

        /// <summary>Значение datetime-local в часовом поясе приложения.</summary>
        public string? DueAt { get; set; }

        public string? InitiatorName { get; set; }

        public Guid? ServiceDepartmentId { get; set; }

        public string? Body { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        Input.InitiatorName = User.FindFirst(CrownAuthHandler.DisplayNameClaim)?.Value;
        Input.DueAt = time.ToInput(time.Now.AddDays(1));
        return await LoadDepartmentsAsync() ?? Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var redirect = await LoadDepartmentsAsync();
        if (redirect is not null)
        {
            return redirect;
        }

        if (string.IsNullOrWhiteSpace(Input.Phone))
        {
            Error = "Телефон обязателен.";
            return Page();
        }

        if (!time.TryParseInput(Input.DueAt, out var dueAt))
        {
            Error = "Укажите срок исполнения (дата и время).";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.InitiatorName))
        {
            Error = "Укажите инициатора.";
            return Page();
        }

        if (Input.ServiceDepartmentId is null || Input.ServiceDepartmentId == Guid.Empty)
        {
            Error = "Выберите службу-исполнителя.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.Body))
        {
            Error = "Опишите заявку.";
            return Page();
        }

        var created = await api.PostAsync<ServiceRequestDetail>(
            "api/requests",
            new CreateServiceRequestRequest
            {
                Phone = Input.Phone.Trim(),
                DueAt = dueAt,
                InitiatorName = Input.InitiatorName.Trim(),
                ServiceDepartmentId = Input.ServiceDepartmentId.Value,
                Body = Input.Body.Trim()
            },
            HttpContext.RequestAborted);

        if (created.IsUnauthorized)
        {
            return Challenge();
        }

        if (!created.Ok || created.Data is null)
        {
            Error = created.Error;
            return Page();
        }

        // Заявка уже создана — сбой вложений не должен терять её, поэтому только предупреждаем.
        var failed = new List<string>();
        foreach (var file in Files.Where(f => f.Length > 0))
        {
            var upload = await api.UploadAsync($"api/requests/{created.Data.Id}/attachments", file, HttpContext.RequestAborted);
            if (!upload.Ok)
            {
                failed.Add($"{file.FileName}: {upload.Error}");
            }
        }

        TempData["Ok"] = $"Заявка № {created.Data.Id} подана.";
        if (failed.Count > 0)
        {
            TempData["Warn"] = "Не все файлы загружены. " + string.Join("; ", failed);
        }

        return Redirect($"/requests/{created.Data.Id}");
    }

    /// <summary>Возвращает редирект на вход, если сессия истекла; иначе null.</summary>
    private async Task<IActionResult?> LoadDepartmentsAsync()
    {
        var result = await api.GetAsync<List<ServiceDepartment>>("api/requests/departments", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error;
            return null;
        }

        Departments = result.Data ?? [];
        return null;
    }
}
