using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Crown.Infrastructure.Api;
using Crown.Infrastructure.Audit;
using Crown.Infrastructure.Authorization;
using Crown.Infrastructure.Backups;
using Crown.Infrastructure.Export;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Import;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Pdf;
using Crown.Infrastructure.Settings;
using Crown.Infrastructure.Time;
using Crown.Infrastructure.Diagnostics;
using Crown.Infrastructure.HostOps;
using Crown.Infrastructure.Warehouse;
using Crown.Infrastructure.Attachments;

var builder = WebApplication.CreateBuilder(args);

PdfFontRegistry.EnsureInitialized();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditEntryFactory>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is not configured. " +
        "Set the ConnectionStrings__DefaultConnection environment variable (see .env.example).");

builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
    options.UseNpgsql(connectionString)
        .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "Crown.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.BusinessData, policy =>
        policy.RequireRole(AppRoles.BusinessUsers));

    options.AddPolicy(AuthorizationPolicies.ManageUsers, policy =>
        policy.RequireRole(AppRoles.CanManageUsers));

    options.AddPolicy(AuthorizationPolicies.ManageWarehouse, policy =>
        policy.RequireRole(AppRoles.CanManageWarehouse));

    options.AddPolicy(AuthorizationPolicies.ManagePartsCatalog, policy =>
        policy.RequireRole(AppRoles.CanManagePartsCatalog));

    options.AddPolicy(AuthorizationPolicies.ViewAuditLog, policy =>
        policy.RequireRole(AppRoles.CanViewAuditLog));

    options.AddPolicy(AuthorizationPolicies.HardDeleteRecords, policy =>
        policy.Requirements.Add(new HardDeleteRequirement()));

    options.AddPolicy(AuthorizationPolicies.ManageBusinessRecords, policy =>
        policy.RequireRole(AppRoles.CanManageBusinessRecords));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Restore / backup / power / update — тяжёлые и опасные операции.
    options.AddPolicy("system-ops", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.Identity?.Name
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

builder.Services.AddSingleton<IAuthorizationHandler, HardDeleteAuthorizationHandler>();
builder.Services.Configure<BackupOptions>(
    builder.Configuration.GetSection(BackupOptions.SectionName));
builder.Services.AddScoped<BackupStatusReader>();
builder.Services.AddSingleton<BackupCatalogService>();
builder.Services.AddSingleton<NetworkInfoReader>();
builder.Services.AddScoped<DatabaseRestoreService>();
builder.Services.AddScoped<ManualBackupService>();
// GitHub-обновление из UI удалено; HostOps — только docker/power/netplan.
builder.Services.Configure<HostOpsOptions>(
    builder.Configuration.GetSection(HostOpsOptions.SectionName));
builder.Services.AddSingleton<HostPowerService>();
builder.Services.AddSingleton<NetplanHostService>();
builder.Services.Configure<SupportOptions>(
    builder.Configuration.GetSection(SupportOptions.SectionName));
builder.Services.AddHttpClient(nameof(SystemDiagnosticsService));
builder.Services.AddScoped<SystemDiagnosticsService>();
builder.Services.AddHostedService<AuditRetentionHostedService>();

builder.Services.AddSingleton<AppTimeZone>();
builder.Services.AddSingleton<WarehouseInventoryPdfGenerator>();
builder.Services.AddScoped<AppSettingsService>();
builder.Services.AddScoped<DataExportService>();
builder.Services.AddScoped<CatalogPartImportService>();
builder.Services.AddScoped<WarehousePartImportService>();
builder.Services.AddScoped<WarehouseStockService>();
builder.Services.Configure<AttachmentStorageOptions>(
    builder.Configuration.GetSection(AttachmentStorageOptions.SectionName));
builder.Services.AddSingleton<AttachmentStorageService>();
builder.Services.AddHostedService<IdentitySeedHostedService>();
builder.Services.AddHostedService<DemoRequestsSeedHostedService>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: ["ready"])
    .AddCheck<DatabaseMigrationHealthCheck>("database_migrations", tags: ["ready"]);

var app = builder.Build();

await DatabaseMigrationBootstrap.ApplyPendingMigrationsAsync(app);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!builder.Configuration.GetValue<bool>("DisableHttpsRedirection"))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapControllers();

app.Run();

public partial class Program;
