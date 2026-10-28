using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages.Requests;

public sealed class DetailsModel(CrownApiClient api, AppTime time) : PageModel
{
    public ServiceRequestDetail? Item { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Введённый комментарий — сохраняется при ошибке, чтобы не пропал.</summary>
    public string? CommentInput { get; private set; }

    public string? NewDueInput { get; private set; }

    /// <summary>Действия над заявкой доступны только админу/диспетчеру (API: ManageBusinessRecords).</summary>
    public bool CanManage =>
        User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);

    /// <summary>
    /// Допустимые действия по статусу. Зеркалит правила RequestsController.Transition:
    /// API на недопустимое действие отвечает 500, поэтому кнопки не показываем заранее.
    /// </summary>
    public static IReadOnlyList<string> ActionsFor(ServiceRequestStatus status) => status switch
    {
        ServiceRequestStatus.Submitted => ["accept", "reject", "return", "cancel"],
        ServiceRequestStatus.InProgress => ["complete", "reject", "return", "reschedule", "cancel"],
        ServiceRequestStatus.Returned => ["accept", "reject", "cancel"],
        _ => []
    };

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var redirect = await LoadAsync(id);
        if (redirect is not null)
        {
            return redirect;
        }

        return Item is null && Error is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostTransitionAsync(
        long id,
        string action,
        string? executorComment,
        string? newDueAt,
        string? rowVersion)
    {
        CommentInput = executorComment;
        NewDueInput = newDueAt;

        var redirect = await LoadAsync(id);
        if (redirect is not null)
        {
            return redirect;
        }

        if (Item is null)
        {
            return Error is null ? NotFound() : Page();
        }

        if (!CanManage || !ActionsFor(Item.Status).Contains(action))
        {
            Error = "Это действие недоступно для текущего состояния заявки.";
            return Page();
        }

        DateTimeOffset? newDue = null;
        if (action == "reschedule")
        {
            if (!time.TryParseInput(newDueAt, out var parsed))
            {
                Error = "Укажите новый срок исполнения.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(executorComment))
            {
                Error = "Для переноса срока нужен комментарий исполнителя.";
                return Page();
            }

            newDue = parsed;
        }

        var result = await api.PostAsync<ServiceRequestDetail>(
            $"api/requests/{id}/transition",
            new TransitionServiceRequestRequest
            {
                Action = action,
                ExecutorComment = executorComment,
                NewDueAt = newDue,
                RowVersion = rowVersion
            },
            HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error;
            return Page();
        }

        TempData["Ok"] = "Заявка обновлена.";
        return Redirect($"/requests/{id}");
    }

    public async Task<IActionResult> OnPostAckAsync(long id, bool ack)
    {
        if (!CanManage)
        {
            return Forbid();
        }

        var result = await api.PostAsync<ServiceRequestDetail>(
            $"api/requests/{id}/transition",
            new TransitionServiceRequestRequest { Action = "acknowledge", Acknowledged = ack },
            HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            TempData["Warn"] = result.Error;
        }
        else
        {
            TempData["Ok"] = ack ? "Отметка «Опер.» поставлена." : "Отметка «Опер.» снята.";
        }

        return Redirect($"/requests/{id}");
    }

    public async Task<IActionResult> OnPostUploadAsync(long id, IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Warn"] = "Выберите файл для загрузки.";
            return Redirect($"/requests/{id}");
        }

        var result = await api.UploadAsync($"api/requests/{id}/attachments", file, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Ok)
        {
            TempData["Ok"] = $"Файл «{file.FileName}» загружен.";
        }
        else
        {
            TempData["Warn"] = result.Error;
        }

        return Redirect($"/requests/{id}");
    }

    /// <summary>Скачивание через Web: работает и без Caddy, cookie прокидывается клиентом.</summary>
    public async Task<IActionResult> OnGetDownloadAsync(long id, Guid attachmentId)
    {
        var response = await api.OpenDownloadAsync(
            $"api/requests/{id}/attachments/{attachmentId}",
            HttpContext.RequestAborted);

        if (response is null)
        {
            TempData["Warn"] = ApiErrorTranslator.DefaultForStatus(0);
            return Redirect($"/requests/{id}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            return status == 401 ? Challenge() : NotFound();
        }

        // Ответ живёт до конца запроса — стримим тело, не буферизуя файл в памяти.
        HttpContext.Response.RegisterForDispose(response);
        var stream = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
        var headers = response.Content.Headers;
        var fileName = headers.ContentDisposition?.FileNameStar
            ?? headers.ContentDisposition?.FileName?.Trim('"')
            ?? $"attachment-{attachmentId}";

        return File(stream, headers.ContentType?.ToString() ?? "application/octet-stream", fileName);
    }

    private async Task<IActionResult?> LoadAsync(long id)
    {
        var result = await api.GetAsync<ServiceRequestDetail>($"api/requests/{id}", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Status == 404)
        {
            Item = null;
            return null;
        }

        if (!result.Ok)
        {
            Error = result.Error;
            Item = null;
            return null;
        }

        Item = result.Data;
        return null;
    }
}
