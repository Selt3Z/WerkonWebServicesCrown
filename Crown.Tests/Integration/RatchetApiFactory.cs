using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WerkonWebServicesRatchet.Infrastructure.Identity;
using WerkonWebServicesRatchet.Infrastructure.Persistence;

namespace WerkonWebServicesRatchet.Tests.Integration;

/// <summary>
/// Boots the real API pipeline with the database swapped to EF InMemory
/// and the migration/seed hosted service removed.
/// </summary>
public sealed class RatchetApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // Сносим все регистрации AppDbContext / Options / OptionsBuilder — иначе Npgsql+InMemory в одном SP.
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
                options.UseInMemoryDatabase($"ratchet-integration-{Guid.NewGuid():N}"));
        });
    }
}
