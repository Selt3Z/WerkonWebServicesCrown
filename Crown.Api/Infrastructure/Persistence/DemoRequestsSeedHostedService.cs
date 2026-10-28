using Crown.Domain;
using Crown.Domain.Entities;
using Crown.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Crown.Infrastructure.Persistence;

/// <summary>
/// Демо-заявки (~100 шт.) — только если ServiceRequests пусто и есть активные инициаторы.
/// Отключить: Seed__DemoRequests=false
/// </summary>
public sealed class DemoRequestsSeedHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<DemoRequestsSeedHostedService> logger) : IHostedService
{
    private static readonly string[] Phones =
    [
        "+7 917 441-12-05", "+7 917 502-33-18", "+7 917 615-44-21", "+7 917 720-55-09",
        "+7 987 301-11-44", "+7 987 412-22-67", "+7 987 523-33-80", "+7 987 634-44-12",
        "+7 937 110-20-30", "+7 937 221-31-42", "+7 937 332-42-53", "+7 937 443-53-64",
        "89174551234", "89175662345", "89871234567", "89371112233"
    ];

    private static readonly string?[] TransportTypes =
    [
        null, null, null, "УАЗ-2206", "УАЗ-3909", "ГАЗ-3308", "КамАЗ-43118",
        "КамАЗ-65115", "вахта", "ВАЗ-21214", "ГАЗель Next", "КрАЗ-255"
    ];

    private static readonly string[] BodyTemplates =
    [
        "Скв. {well} куст {pad}. Отказ ЭЦН, ток холостого хода. Требуется подъём и замена ПЭД.",
        "Скв. {well} куст {pad}. Прорыв НКТ на глубине ~{depth} м. Подготовить бригаду КРС.",
        "Скв. {well}. Забивка пакера / негерметичность. Нужен спуск пакера, опрессовка.",
        "Куст {pad}, скв. {well}. Глушение перед КРС: объём ~{vol} м³.",
        "Скв. {well}. ПРС: смена насоса НВН, протартать до {depth} м.",
        "Авария: скв. {well} куст {pad}. Разгерметизация арматуры. Выезд бригады.",
        "Куст {pad}. Отключение ВЛ-6 кВ, без напряжения ЭЦН на скв. {well}.",
        "Трубная база: подобрать НКТ под скв. {well}, отгрузка на куст {pad}.",
        "БПО: подготовить оборудование под КРС скв. {well}.",
        "КИПиА: поверка манометра на устье скв. {well}.",
        "ЦИТС: не проходит телеметрия скв. {well}. Проверить модем на кусте {pad}.",
        "Скв. {well} куст {pad}. Не держит пакер. Повторный спуск / ревизия."
    ];

    private static readonly string?[] ExecutorComments =
    [
        "Принято в работу, бригада выезжает.",
        "Ожидаем технику с базы.",
        "Согласовано с ЦИТС.",
        "Выполнено, акт передан мастеру.",
        "Требуется уточнение объёма работ.",
        "Перенос: нет свободной бригады.",
        "Отклонено: нет согласования ПТО.",
        null,
        null
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var enabled = configuration.GetValue("Seed:DemoRequests", true);
        if (!enabled)
        {
            return;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await db.ServiceRequests.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            return;
        }

        var assignees = (await userManager.GetUsersInRoleAsync(AppRoles.Initiator))
            .Where(x => x.IsActive)
            .ToList();

        if (assignees.Count == 0)
        {
            logger.LogWarning("Demo requests seed skipped: нет активных пользователей с ролью Initiator.");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var rnd = new Random(20261028);
        const int count = 100;
        var entities = new List<ServiceRequest>(count);

        for (var i = 0; i < count; i++)
        {
            var initiator = assignees[rnd.Next(assignees.Count)];
            ApplicationUser executor;
            do
            {
                executor = assignees[rnd.Next(assignees.Count)];
            } while (assignees.Count > 1 && executor.Id == initiator.Id);

            var well = 1100 + rnd.Next(1, 900);
            var pad = rnd.Next(1, 48);
            var depth = 800 + rnd.Next(0, 1400);
            var vol = 5 + rnd.Next(0, 40);
            var body = BodyTemplates[i % BodyTemplates.Length]
                .Replace("{well}", well.ToString())
                .Replace("{pad}", pad.ToString())
                .Replace("{depth}", depth.ToString())
                .Replace("{vol}", vol.ToString());

            if (!PhoneNumberNormalizer.TryNormalize(Phones[rnd.Next(Phones.Length)], out var phone, out _))
            {
                phone = "+79170000000";
            }

            var submitted = now.AddDays(-rnd.Next(0, 75)).AddHours(-rnd.Next(0, 20));
            var due = submitted.AddHours(8 + rnd.Next(0, 72));
            var statusRoll = rnd.Next(100);

            ServiceRequestStatus status;
            DateTimeOffset? reaction = null;
            DateTimeOffset? completed = null;
            var acknowledged = false;
            string? comment = null;

            if (statusRoll < 18)
            {
                status = ServiceRequestStatus.Submitted;
            }
            else if (statusRoll < 42)
            {
                status = ServiceRequestStatus.InProgress;
                reaction = submitted.AddHours(1 + rnd.Next(0, 12));
                acknowledged = true;
                comment = ExecutorComments[rnd.Next(ExecutorComments.Length)];
            }
            else if (statusRoll < 72)
            {
                status = ServiceRequestStatus.Completed;
                reaction = submitted.AddHours(1 + rnd.Next(0, 8));
                completed = reaction.Value.AddHours(2 + rnd.Next(0, 48));
                if (completed > now)
                {
                    completed = now.AddMinutes(-rnd.Next(5, 180));
                }

                acknowledged = true;
                comment = "Выполнено, акт передан мастеру.";
            }
            else if (statusRoll < 82)
            {
                status = ServiceRequestStatus.Returned;
                reaction = submitted.AddHours(2 + rnd.Next(0, 24));
                acknowledged = true;
                comment = "Требуется уточнение объёма работ.";
            }
            else if (statusRoll < 92)
            {
                status = ServiceRequestStatus.Rejected;
                reaction = submitted.AddHours(1 + rnd.Next(0, 20));
                acknowledged = true;
                comment = "Отклонено: нет согласования ПТО.";
            }
            else
            {
                status = ServiceRequestStatus.Cancelled;
                reaction = submitted.AddHours(rnd.Next(1, 10));
                acknowledged = true;
                comment = "Отменено инициатором.";
            }

            entities.Add(new ServiceRequest
            {
                SubmittedAt = submitted,
                Phone = phone,
                DueAt = due,
                InitiatorName = string.IsNullOrWhiteSpace(initiator.DisplayName)
                    ? (initiator.UserName ?? "Инициатор")
                    : initiator.DisplayName,
                InitiatorUserId = initiator.Id,
                ExecutorName = string.IsNullOrWhiteSpace(executor.DisplayName)
                    ? (executor.UserName ?? "Исполнитель")
                    : executor.DisplayName,
                ExecutorUserId = executor.Id,
                TransportType = TransportTypes[rnd.Next(TransportTypes.Length)],
                Body = body,
                ExecutorComment = comment,
                Status = status,
                Acknowledged = acknowledged,
                ReactionAt = reaction,
                CompletedAt = completed,
                CreatedByUserId = initiator.Id,
                IsArchived = false
            });
        }

        entities = entities.OrderBy(x => x.SubmittedAt).ToList();
        db.ServiceRequests.AddRange(entities);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} demo service requests.", entities.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
