using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;

namespace Crown.Tests.Integration;

/// <summary>Реальный pipeline API, EF → InMemory, без identity-seed.</summary>
public sealed class CrownApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection",
            "Host=127.0.0.1;Port=5432;Database=crown_test;Username=u;Password=p");
        builder.UseSetting("DisableHttpsRedirection", "true");

        builder.ConfigureServices(services =>
        {
            var toRemove = services
                .Where(d =>
                    d.ServiceType == typeof(AppDbContext)
                    || d.ServiceType == typeof(DbContextOptions)
                    || d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                    || (d.ServiceType.IsGenericType
                        && d.ServiceType.GetGenericTypeDefinition() == typeof(IDbContextOptionsConfiguration<>)
                        && d.ServiceType.GenericTypeArguments[0] == typeof(AppDbContext)))
                .ToList();

            foreach (var descriptor in toRemove)
            {
                services.Remove(descriptor);
            }

            var seedService = services.SingleOrDefault(x =>
                x.ServiceType == typeof(IHostedService)
                && x.ImplementationType == typeof(IdentitySeedHostedService));
            if (seedService is not null)
            {
                services.Remove(seedService);
            }

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase($"crown-integration-{Guid.NewGuid():N}"));
        });
    }
}
