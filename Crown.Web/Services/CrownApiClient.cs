using System.Text.Json;
using System.Text.Json.Serialization;
using Crown.Web.Models;
using Microsoft.Net.Http.Headers;

namespace Crown.Web.Services;

public sealed record LoginOutcome(bool Success, CurrentUserResponse? User, string? Error);

/// <summary>
/// Тонкий клиент Crown.Api. Auth-cookie (Crown.Auth*) принадлежит API: Web её не создаёт,
/// а только (1) прокидывает из браузерного запроса в запрос к API и
/// (2) копирует Set-Cookie из ответов API обратно в ответ браузеру.
/// Так сессия остаётся единой и работает и за Caddy, и без него.
/// </summary>
public sealed class CrownApiClient
{
    public const string AuthCookiePrefix = "Crown.Auth";
    private const string CurrentUserItemKey = "Crown.Web.CurrentUser";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _accessor;
    private readonly ILogger<CrownApiClient> _logger;

    public CrownApiClient(HttpClient http, IHttpContextAccessor accessor, ILogger<CrownApiClient> logger)
    {
        _http = http;
        _accessor = accessor;
        _logger = logger;
    }

    private HttpContext? Ctx => _accessor.HttpContext;

    /// <summary>Есть ли в запросе браузера auth-cookie (без обращения к API).</summary>
    public bool HasAuthCookie => BuildCookieHeader() is not null;

    // ---------- Авторизация ----------

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var ctx = Ctx;
        if (ctx?.Items[CurrentUserItemKey] is CurrentUserResponse cached)
        {
            return cached;
        }

        if (!HasAuthCookie)
        {
            return null;
        }

        var result = await GetAsync<CurrentUserResponse>("api/auth/me", ct);
        if (!result.Ok || result.Data is null)
        {
            return null;
        }

        if (ctx is not null)
        {
            ctx.Items[CurrentUserItemKey] = result.Data;
        }

        return result.Data;
    }

    public async Task<LoginOutcome> LoginAsync(string userName, string password, CancellationToken ct = default)
    {
        try
        {
            // Старые cookie в запрос логина не прокидываем.
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
            {
                Content = JsonContent.Create(
                    new LoginRequest { UserName = userName, Password = password },
                    mediaType: null,
                    options: JsonOptions)
            };

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var message = response.StatusCode == System.Net.HttpStatusCode.Locked
                    ? BuildLockoutMessage(body)
                    : ApiErrorTranslator.FromBody((int)response.StatusCode, body);
                return new LoginOutcome(false, null, message);
            }

            ForwardSetCookies(response);
            var user = JsonSerializer.Deserialize<CurrentUserResponse>(body, JsonOptions);
            return new LoginOutcome(true, user, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Не удалось выполнить вход через API");
            return new LoginOutcome(false, null, ApiErrorTranslator.DefaultForStatus(0));
        }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        // Результат неважен: cookie в браузере всё равно удаляется страницей выхода.
        await SendAsync<NoContent>(HttpMethod.Post, "api/auth/logout", null, ct);
    }

    private string BuildLockoutMessage(string body)
    {
        var text = ApiErrorTranslator.FromBody(423, body);
        try
        {
            var failure = JsonSerializer.Deserialize<LoginFailureResponse>(body, JsonOptions);
            if (failure?.LockoutEndUtc is { } end)
            {
                var minutes = (int)Math.Ceiling((end - DateTimeOffset.UtcNow).TotalMinutes);
                if (minutes > 0)
                {
                    text += $" Повторите попытку через {minutes} мин.";
                }
            }
        }
        catch (JsonException)
        {
            // Без времени блокировки — достаточно общего текста.
        }

        return text;
    }

    // ---------- JSON-вызовы ----------

    public Task<ApiResult<T>> GetAsync<T>(string url, CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Get, url, null, ct);

    public Task<ApiResult<T>> PostAsync<T>(string url, object? body, CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Post, url, body, ct);

    public Task<ApiResult<T>> PutAsync<T>(string url, object? body, CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Put, url, body, ct);

    public Task<ApiResult<T>> PatchAsync<T>(string url, object? body = null, CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Patch, url, body, ct);

    public Task<ApiResult<NoContent>> DeleteAsync(string url, CancellationToken ct = default) =>
        SendAsync<NoContent>(HttpMethod.Delete, url, null, ct);

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        try
        {
            using var request = CreateRequest(method, url);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, body.GetType(), null, JsonOptions);
            }

            return await ExecuteAsync<T>(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Ошибка вызова API {Method} {Url}", method, url);
            return ApiResult<T>.Failure(0, ApiErrorTranslator.DefaultForStatus(0));
        }
    }

    /// <summary>Загрузка файла в API (поле формы называется file — как IFormFile file в контроллере).</summary>
    public Task<ApiResult<RequestAttachment>> UploadAsync(string url, IFormFile file, CancellationToken ct = default) =>
        UploadFileAsync<RequestAttachment>(url, file, "file", ct);

    public async Task<ApiResult<T>> UploadFileAsync<T>(
        string url,
        IFormFile file,
        string fieldName = "file",
        CancellationToken ct = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Post, url);
            using var form = new MultipartFormDataContent();
            await using var stream = file.OpenReadStream();
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(file.ContentType, out var ct2)
                ? ct2
                : new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, fieldName, Path.GetFileName(file.FileName));
            request.Content = form;

            return await ExecuteAsync<T>(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Ошибка загрузки файла в API {Url}", url);
            return ApiResult<T>.Failure(0, ApiErrorTranslator.DefaultForStatus(0));
        }
    }

    /// <summary>
    /// Открывает поток для скачивания. Вызывающий обязан освободить ответ
    /// (Response.RegisterForDispose). Null — API недоступен.
    /// </summary>
    public async Task<HttpResponseMessage?> OpenDownloadAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            ForwardSetCookies(response);
            return response;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Ошибка скачивания из API {Url}", url);
            return null;
        }
    }

    private async Task<ApiResult<T>> ExecuteAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _http.SendAsync(request, ct);
        ForwardSetCookies(response);

        var text = await response.Content.ReadAsStringAsync(ct);
        var status = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            return ApiResult<T>.Failure(status, ApiErrorTranslator.FromBody(status, text));
        }

        if (typeof(T) == typeof(NoContent) || string.IsNullOrWhiteSpace(text))
        {
            return ApiResult<T>.Success(default, status);
        }

        try
        {
            return ApiResult<T>.Success(JsonSerializer.Deserialize<T>(text, JsonOptions), status);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Не удалось разобрать ответ API");
            return ApiResult<T>.Failure(status, "Не удалось разобрать ответ сервера.");
        }
    }

    // ---------- Cookie ----------

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        var cookies = BuildCookieHeader();
        if (cookies is not null)
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.Cookie, cookies);
        }

        return request;
    }

    /// <summary>
    /// Берём «сырой» заголовок Cookie, а не Request.Cookies: коллекция декодирует значения,
    /// а API должен получить cookie ровно в том виде, в котором её выдал.
    /// </summary>
    private string? BuildCookieHeader()
    {
        var raw = Ctx?.Request.Headers.Cookie.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parts = raw.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.StartsWith(AuthCookiePrefix, StringComparison.Ordinal))
            .ToList();

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private void ForwardSetCookies(HttpResponseMessage response)
    {
        var ctx = Ctx;
        if (ctx is null || ctx.Response.HasStarted)
        {
            return;
        }

        if (!response.Headers.TryGetValues(HeaderNames.SetCookie, out var values))
        {
            return;
        }

        foreach (var value in values)
        {
            if (!value.StartsWith(AuthCookiePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            // API за Caddy видит http и не ставит Secure; если браузер пришёл по https — добавляем сами.
            var cookie = ctx.Request.IsHttps && !value.Contains("secure", StringComparison.OrdinalIgnoreCase)
                ? value + "; secure"
                : value;
            ctx.Response.Headers.Append(HeaderNames.SetCookie, cookie);
        }
    }

    /// <summary>Удаляет auth-cookie в браузере (на случай, если API не прислал Set-Cookie).</summary>
    public void ExpireAuthCookies()
    {
        var ctx = Ctx;
        if (ctx is null || ctx.Response.HasStarted)
        {
            return;
        }

        var raw = ctx.Request.Headers.Cookie.ToString();
        var names = raw.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)[0])
            .Where(n => n.StartsWith(AuthCookiePrefix, StringComparison.Ordinal))
            .Distinct();

        foreach (var name in names)
        {
            ctx.Response.Cookies.Delete(name, new CookieOptions { Path = "/" });
        }
    }
}
