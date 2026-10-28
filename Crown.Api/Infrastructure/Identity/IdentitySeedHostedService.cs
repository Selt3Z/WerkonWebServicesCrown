using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Crown.Domain.Entities;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Settings;
using Crown.Infrastructure.Time;

namespace Crown.Infrastructure.Identity;

public sealed class IdentitySeedHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IdentitySeedHostedService> _logger;

    public IdentitySeedHostedService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<IdentitySeedHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var roleName in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            }
        }

        var adminUserName = _configuration["Seed:AdminUserName"] ?? "admin";
        var adminPassword = _configuration["Seed:AdminPassword"];
        var adminDisplayName = _configuration["Seed:AdminDisplayName"] ?? "Administrator";

        var adminUser = await userManager.FindByNameAsync(adminUserName);

        if (adminUser is null)
        {
            if (string.IsNullOrWhiteSpace(adminPassword))
            {
                throw new InvalidOperationException(
                    $"Seed admin user '{adminUserName}' does not exist and 'Seed:AdminPassword' is not configured. " +
                    "Set the Seed__AdminPassword environment variable (see .env.example).");
            }

            adminUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = adminUserName,
                DisplayName = adminDisplayName,
                Email = $"{adminUserName}@local",
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(adminUser, adminPassword);

            if (!createResult.Succeeded)
            {
                _logger.LogWarning(
                    "Failed to create seed admin user {UserName}: {Errors}",
                    adminUserName,
                    string.Join(", ", createResult.Errors.Select(x => x.Description)));
                return;
            }

            _logger.LogInformation("Seed admin user {UserName} was created.", adminUserName);
        }

        if (!await userManager.IsInRoleAsync(adminUser, AppRoles.Administrator))
        {
            await userManager.AddToRoleAsync(adminUser, AppRoles.Administrator);
        }

        if (!adminUser.IsActive)
        {
            adminUser.IsActive = true;
            await userManager.UpdateAsync(adminUser);
            _logger.LogWarning("Seed admin user {UserName} was inactive and has been reactivated.", adminUserName);
        }

        var defaultTimeZoneId = _configuration[AppSettingKeys.TimeZone] ?? "Europe/Moscow";
        var timeZoneSetting = await dbContext.AppSettings
            .FirstOrDefaultAsync(x => x.Key == AppSettingKeys.TimeZone, cancellationToken);

        if (timeZoneSetting is null)
        {
            dbContext.AppSettings.Add(new AppSetting
            {
                Key = AppSettingKeys.TimeZone,
                Value = defaultTimeZoneId
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            timeZoneSetting = await dbContext.AppSettings
                .FirstAsync(x => x.Key == AppSettingKeys.TimeZone, cancellationToken);
        }

        var defaultTheme = _configuration[AppSettingKeys.Theme] ?? "light";
        var themeSetting = await dbContext.AppSettings
            .FirstOrDefaultAsync(x => x.Key == AppSettingKeys.Theme, cancellationToken);

        if (themeSetting is null)
        {
            dbContext.AppSettings.Add(new AppSetting
            {
                Key = AppSettingKeys.Theme,
                Value = defaultTheme
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await SeedSettingIfMissingAsync(dbContext, AppSettingKeys.CurrencyCode, AppSettingsDefaults.CurrencyCode, cancellationToken);
        await SeedSettingIfMissingAsync(dbContext, AppSettingKeys.HideArchivedByDefault, AppSettingsDefaults.HideArchivedByDefault.ToString(), cancellationToken);
        await SeedSettingIfMissingAsync(dbContext, AppSettingKeys.DispatcherCanHardDelete, AppSettingsDefaults.DispatcherCanHardDelete.ToString(), cancellationToken);
        await SeedSettingIfMissingAsync(dbContext, AppSettingKeys.WarehouseEnabled, AppSettingsDefaults.WarehouseEnabled.ToString(), cancellationToken);
        await SeedSettingIfMissingAsync(dbContext, AppSettingKeys.AuditRetentionDays, AppSettingsDefaults.AuditRetentionDays.ToString(), cancellationToken);
        await SeedSettingIfMissingAsync(dbContext, AppSettingKeys.ListPageSize, AppSettingsDefaults.ListPageSize.ToString(), cancellationToken);
        var appTimeZone = scope.ServiceProvider.GetRequiredService<AppTimeZone>();
        appTimeZone.SetTimeZoneId(timeZoneSetting.Value);
    }

    private static async Task SeedSettingIfMissingAsync(
        AppDbContext dbContext,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.AppSettings.AnyAsync(x => x.Key == key, cancellationToken);

        if (exists)
        {
            return;
        }

        dbContext.AppSettings.Add(new AppSetting
        {
            Key = key,
            Value = value
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
