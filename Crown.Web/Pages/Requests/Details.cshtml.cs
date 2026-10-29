using System.Security.Claims;
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

    public string? ComplaintTextInput { get; private set; }

    public string? BodyInput { get; private set; }

    public bool ShowComplaintForm { get; private set; }

    /// <summary>Из настроек: диспетчер может hard-delete.</summary>
    public bool DispatcherCanHardDelete { get; private set; }

    public bool IsInitiatorOfItem =>
        Item?.InitiatorUserId is Guid iid
        && User.FindFirstValue(ClaimTypes.NameIdentifier) == iid.ToString();

    public bool IsExecutorOfItem =>
        Item?.ExecutorUserId is Guid eid
        && User.FindFirstValue(ClaimTypes.NameIdentifier) == eid.ToString();

    /// <summary>Workflow: админ / диспетчер (прозапас) / исполнитель заявки.</summary>
    public bool CanManage =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || IsExecutorOfItem;

    /// <summary>Ручной toggle «Ознак.» — только администратор.</summary>
    public bool CanToggleAck => User.IsInRole(AppRoles.Administrator);

    /// <summary>Архив: инициатор своей заявки или админ/диспетчер.</summary>
    public bool CanArchive =>
        Item is not null
        && (IsInitiatorOfItem
            || User.IsInRole(AppRoles.Administrator)
            || User.IsInRole(AppRoles.Dispatcher));

    /// <summary>Удаление безвозвратно: админ; диспетчер — по галочке в настройках.</summary>
    public bool CanHardDelete =>
        User.IsInRole(AppRoles.Administrator)
        || (User.IsInRole(AppRoles.Dispatcher) && DispatcherCanHardDelete);

    /// <summary>Инициатор может отменить свою заявку, пока она не в терминальном статусе.</summary>
    public bool CanCancelAsInitiator =>
        IsInitiatorOfItem
        && Item is not null
        && Item.Status is ServiceRequestStatus.Submitted
            or ServiceRequestStatus.InProgress
            or ServiceRequestStatus.Returned;

    /// <summary>Возвращённую заявку инициатор правит и подаёт снова.</summary>
    public bool CanResubmit =>
        IsInitiatorOfItem && Item?.Status == ServiceRequestStatus.Returned;

    /// <summary>Жалоба: инициатор на возврат / отклонение / выполнено.</summary>
    public bool CanFileComplaint =>
        IsInitiatorOfItem
        && Item is not null
        && !Item.HasComplaint
        && Item.Status is ServiceRequestStatus.Returned
            or ServiceRequestStatus.Rejected
            or ServiceRequestStatus.Completed;

    /// <summary>
    /// Допустимые действия по статусу. Админ может менять и закрытые заявки (reopen).
    /// Вернуть / отклонить / перенос — только с комментарием (проверяется на Web и API).
    /// </summary>
    public static IReadOnlyList<string> ActionsFor(ServiceRequestStatus status, bool isAdmin = false) => status switch
    {
        ServiceRequestStatus.Submitted => ["accept", "reject", "return", "cancel"],
        ServiceRequestStatus.InProgress => ["complete", "reject", "return", "reschedule", "cancel"],
        ServiceRequestStatus.Returned => ["accept", "reject", "cancel"],
        // Закрытые: админ может вернуть в работу / отменить / вернуть инициатору
        ServiceRequestStatus.Rejected when isAdmin => ["accept", "cancel"],
        ServiceRequestStatus.Completed when isAdmin => ["accept", "return", "cancel"],
        ServiceRequestStatus.Cancelled when isAdmin => ["accept"],
        _ => []
    };

    public async Task<IActionResult> OnGetAsync(long id, bool complaint = false)
    {
        ShowComplaintForm = complaint;
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
        string? rowVersion,
        string? body)
    {
        CommentInput = executorComment;
        NewDueInput = newDueAt;
        BodyInput = body;

        var redirect = await LoadAsync(id);
        if (redirect is not null)
        {
            return redirect;
        }

        if (Item is null)
        {
            return Error is null ? NotFound() : Page();
        }

        var isAdmin = User.IsInRole(AppRoles.Administrator);
        var managerOk = CanManage && ActionsFor(Item.Status, isAdmin).Contains(action);
        var initiatorCancelOk = action == "cancel" && CanCancelAsInitiator;
        var initiatorResubmitOk = action == "resubmit" && CanResubmit;
        if (!managerOk && !initiatorCancelOk && !initiatorResubmitOk)
        {
            Error = "Это действие недоступно для текущего состояния заявки.";
            return Page();
        }

        if (action is "reject" or "return" or "reschedule")
        {
            if (string.IsNullOrWhiteSpace(executorComment))
            {
                Error = action switch
                {
                    "reject" => "Чтобы отклонить заявку, нужен комментарий.",
                    "return" => "Чтобы вернуть заявку, нужен комментарий.",
                    _ => "Для переноса срока нужен комментарий исполнителя."
                };
                return Page();
            }
        }

        if (action == "resubmit" && string.IsNullOrWhiteSpace(body))
        {
            Error = "Укажите текст заявки.";
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

            newDue = parsed;
        }

        var result = await api.PostAsync<ServiceRequestDetail>(
            $"api/requests/{id}/transition",
            new TransitionServiceRequestRequest
            {
                Action = action,
                ExecutorComment = executorComment,
                Body = body,
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

    public async Task<IActionResult> OnPostComplaintAsync(
        long id,
        string? complaintText,
        bool complaintConfirmed)
    {
        ComplaintTextInput = complaintText;
        ShowComplaintForm = true;

        var redirect = await LoadAsync(id);
        if (redirect is not null)
        {
            return redirect;
        }

        if (Item is null)
        {
            return Error is null ? NotFound() : Page();
        }

        if (!CanFileComplaint)
        {
            Error = "Жалобу может подать только инициатор по возвращённой или отклонённой заявке.";
            ShowComplaintForm = false;
            return Page();
        }

        if (!complaintConfirmed)
        {
            Error = "Подтвердите, что жалоба обоснована.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(complaintText))
        {
            Error = "Опишите суть жалобы.";
            return Page();
        }

        var result = await api.PostAsync<ServiceRequestDetail>(
            $"api/requests/{id}/complaint",
            new FileComplaintRequest { Text = complaintText.Trim(), Confirmed = true },
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

        TempData["Ok"] = "Жалоба подана.";
        return Redirect($"/requests/{id}");
    }

    public async Task<IActionResult> OnPostAckAsync(long id, bool ack)
    {
        if (!CanToggleAck)
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
            TempData["Ok"] = ack ? "Отметка «Ознак.» поставлена." : "Отметка «Ознак.» снята.";
        }

        return Redirect($"/requests/{id}");
    }

    public async Task<IActionResult> OnPostArchiveAsync(long id, bool archive)
    {
        var redirect = await LoadAsync(id);
        if (redirect is not null)
        {
            return redirect;
        }

        if (Item is null)
        {
            return Error is null ? NotFound() : Page();
        }

        if (!CanArchive)
        {
            return Forbid();
        }

        var path = archive ? $"api/requests/{id}/archive" : $"api/requests/{id}/unarchive";
        var result = await api.PatchAsync<NoContent>(path, null, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            TempData["Warn"] = result.Error;
            return Redirect($"/requests/{id}");
        }

        TempData["Ok"] = archive ? "Заявка в архиве." : "Заявка восстановлена из архива.";
        return archive ? Redirect("/") : Redirect($"/requests/{id}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id)
    {
        var redirect = await LoadAsync(id);
        if (redirect is not null)
        {
            return redirect;
        }

        if (Item is null)
        {
            return Error is null ? NotFound() : Page();
        }

        if (!CanHardDelete)
        {
            return Forbid();
        }

        var result = await api.DeleteAsync($"api/requests/{id}", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            TempData["Warn"] = result.Error;
            return Redirect($"/requests/{id}");
        }

        TempData["Ok"] = $"Заявка № {id} удалена безвозвратно.";
        return Redirect("/");
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
    public async Task<IActionResult> OnGetDownloadAsync(long id, Guid attachmentId, bool inline = false)
    {
        var apiUrl = inline
            ? $"api/requests/{id}/attachments/{attachmentId}?inline=true"
            : $"api/requests/{id}/attachments/{attachmentId}";
        var response = await api.OpenDownloadAsync(apiUrl, HttpContext.RequestAborted);

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

        HttpContext.Response.RegisterForDispose(response);
        var stream = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
        var headers = response.Content.Headers;
        var contentType = headers.ContentType?.ToString() ?? "application/octet-stream";
        if (inline)
        {
            return File(stream, contentType);
        }

        var fileName = headers.ContentDisposition?.FileNameStar
            ?? headers.ContentDisposition?.FileName?.Trim('"')
            ?? $"attachment-{attachmentId}";

        return File(stream, contentType, fileName);
    }

    private async Task<IActionResult?> LoadAsync(long id)
    {
        var settingsTask = api.GetAsync<SettingsResponse>("api/settings", HttpContext.RequestAborted);
        var result = await api.GetAsync<ServiceRequestDetail>($"api/requests/{id}", HttpContext.RequestAborted);
        var settings = await settingsTask;
        if (settings.Ok && settings.Data is not null)
        {
            DispatcherCanHardDelete = settings.Data.DispatcherCanHardDelete;
        }

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
