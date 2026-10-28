using Crown.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Crown.Infrastructure.Persistence;

/// <summary>Справочник служб из черновика UI заказчика.</summary>
public sealed class ServiceDepartmentSeedHostedService(IServiceProvider services) : IHostedService
{
    private static readonly string[] Departments =
    [
        "Аварийная служба",
        "Служба гл. энергетика",
        "Служба ремонта ПА",
        "Трубная база",
        "Плотники",
        "ЦИТС",
        "БПО",
        "КИПиА",
        "Главный технолог",
        "ЦИТС ПРС",
        "Главный геолог",
        "Цех КРС",
        "Цех ПРС",
        "Подготовка ПиКРС"
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.ServiceDepartments.AnyAsync(cancellationToken))
        {
            return;
        }

        for (var i = 0; i < Departments.Length; i++)
        {
            db.ServiceDepartments.Add(new ServiceDepartment
            {
                Id = Guid.NewGuid(),
                Name = Departments[i],
                SortOrder = i + 1,
                IsActive = true
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
