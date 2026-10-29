using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

public sealed class IndexModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? InitiatorUserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? ExecutorUserId { get; set; }

    /// <summary>null = все; true = ознакомлен; false = не ознакомлен.</summary>
    [BindProperty(SupportsGet = true)]
    public bool? Ack { get; set; }

    /// <summary>Показать архивные (связано с «Скрывать архивные по умолчанию»).</summary>
    public bool ShowArchived { get; private set; }

    public List<ServiceRequestListItem> Items { get; private set; } = [];

    public List<RequestAssignee> Assignees { get; private set; } = [];

    public bool HasMore { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        NormalizeSort();
        await ResolveShowArchivedAsync();
        await LoadAssigneesAsync();
        var (items, hasMore, error, challenge) = await LoadAsync(0);
        if (challenge)
        {
            return Challenge();
        }

        Items = items;
        HasMore = hasMore;
        Error = error;
        return Page();
    }

    public async Task<IActionResult> OnGetMoreAsync(int skip)
    {
        NormalizeSort();
        await ResolveShowArchivedAsync();
        if (skip < 0)
        {
            skip = 0;
        }

        var (items, hasMore, _, challenge) = await LoadAsync(skip);
        if (challenge)
        {
            return Challenge();
        }

        Response.Headers["X-Has-More"] = hasMore ? "true" : "false";
        return Partial("_RequestRows", items);
    }

    public async Task<IActionResult> OnGetMoreCardsAsync(int skip)
    {
        NormalizeSort();
        await ResolveShowArchivedAsync();
        if (skip < 0)
        {
            skip = 0;
        }

        var (items, hasMore, _, challenge) = await LoadAsync(skip);
        if (challenge)
        {
            return Challenge();
        }

        Response.Headers["X-Has-More"] = hasMore ? "true" : "false";
        return Partial("_RequestCards", items);
    }

    public string SortUrl(string column) =>
        UiHelpers.BuildUrl(
            "/",
            ("q", Q),
            ("status", Status?.ToString()),
            ("initiatorUserId", InitiatorUserId?.ToString()),
            ("executorUserId", ExecutorUserId?.ToString()),
            ("ack", Ack.HasValue ? (Ack.Value ? "true" : "false") : null),
            ("showArchived", ShowArchived ? "true" : "false"),
            ("sort", column),
            ("dir", UiHelpers.NextSortDir(column, Sort, Dir)));

    private void NormalizeSort()
    {
        var key = (Sort ?? string.Empty).Trim().ToLowerInvariant();
        Sort = key is "initiator" or "executor" or "department" or "id" ? key : null;
        if (Sort == "department")
        {
            Sort = "executor";
        }

        if (Sort is null)
        {
            Dir = null;
            return;
        }

        Dir = string.Equals(Dir, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";
    }

    /// <summary>
    /// В query может быть showArchived=false&amp;true (hidden+checkbox) — true, если хоть одно true.
    /// Нет параметра — настройка HideArchivedByDefault.
    /// </summary>
    private async Task ResolveShowArchivedAsync()
    {
        if (Request.Query.TryGetValue("showArchived", out var values))
        {
            ShowArchived = values.Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));
            return;
        }

        var settings = await api.GetAsync<SettingsResponse>("api/settings", HttpContext.RequestAborted);
        if (settings.Ok && settings.Data is not null)
        {
            ShowArchived = !settings.Data.HideArchivedByDefault;
        }
    }

    private async Task LoadAssigneesAsync()
    {
        var result = await api.GetAsync<List<RequestAssignee>>("api/requests/assignees", HttpContext.RequestAborted);
        if (result.Ok && result.Data is not null)
        {
            Assignees = result.Data;
        }
    }

    private async Task<(List<ServiceRequestListItem> Items, bool HasMore, string? Error, bool Challenge)> LoadAsync(int skip)
    {
        var url = UiHelpers.BuildUrl(
            "api/requests",
            ("search", Q?.Trim()),
            ("sort", Sort),
            ("dir", Dir),
            ("status", Status?.ToString()),
            ("initiatorUserId", InitiatorUserId?.ToString()),
            ("executorUserId", ExecutorUserId?.ToString()),
            ("acknowledged", Ack.HasValue ? (Ack.Value ? "true" : "false") : null),
            ("includeArchived", ShowArchived ? "true" : "false"),
            ("skip", skip.ToString()),
            ("take", UiHelpers.PageSize.ToString()));

        var result = await api.GetAsync<PagedResponse<ServiceRequestListItem>>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return ([], false, null, true);
        }

        if (!result.Ok || result.Data is null)
        {
            return ([], false, result.Error, false);
        }

        return (result.Data.Items, result.Data.HasMore, null, false);
    }
}
