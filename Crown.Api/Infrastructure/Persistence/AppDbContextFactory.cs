using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crown.Infrastructure.Persistence;

/// <summary>Design-time фабрика для `dotnet ef migrations` без поднятия всего host.</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=crown_db;Username=postgres;Password=postgres")
            .Options;

        return new AppDbContext(options);
    }
}
