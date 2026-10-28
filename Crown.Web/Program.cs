using Crown.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
{
    // Всё закрыто, кроме страниц входа/ошибок; роли проверяются атрибутами [Authorize(Roles = ...)] на PageModel.
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/AccessDenied");
    options.Conventions.AllowAnonymousToPage("/Error");
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AppTime>();

builder.Services
    .AddAuthentication(CrownAuthHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, CrownAuthHandler>(
        CrownAuthHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

var apiBaseUrl = builder.Configuration["CrownApiBaseUrl"] ?? "http://localhost:5277/";
builder.Services.AddHttpClient<CrownApiClient>(client =>
    {
        client.BaseAddress = new Uri(apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/");
        client.Timeout = TimeSpan.FromSeconds(60);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        // Cookie ведём вручную (прокидываем из браузера), встроенный контейнер не нужен.
        UseCookies = false,
        AllowAutoRedirect = false,
        // Внутренний API: системный/env-прокси здесь только мешает (на dev-машинах отдаёт 502).
        UseProxy = false
    });

// Ключи DataProtection (antiforgery) переживают пересоздание контейнера, если смонтирован том.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    Directory.CreateDirectory(keysPath);
    builder.Services.AddDataProtection()
        .SetApplicationName("Crown.Web")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Web стоит только за своим Caddy во внутренней docker-сети.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
}

// HTTPS-редиректа нет намеренно: TLS терминирует Caddy.
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapRazorPages();

app.Run();
