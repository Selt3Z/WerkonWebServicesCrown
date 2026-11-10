using Crown.Contracts.Summary;
using Crown.Domain;
using Crown.Domain.Entities;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Crown.Features.Summary;

public sealed class SummaryReportService
{
    private readonly AppDbContext _db;
    private readonly AppTimeZone _time;
    private readonly UserManager<ApplicationUser> _users;

    public SummaryReportService(AppDbContext db, AppTimeZone time, UserManager<ApplicationUser> users)
    {
        _db = db;
        _time = time;
        _users = users;
    }

    public async Task<SummaryResponse?> BuildAsync(
        Guid userId,
        string? period,
        int? year,
        int? month,
        bool includeUserPicker,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var normalizedPeriod = NormalizePeriod(period);
        var (startUtc, endUtc, startLocal, endLocal) = GetPeriodRange(normalizedPeriod, year, month);
        var (prevStartUtc, prevEndUtc, prevStartLocal, prevEndLocal) =
            GetPreviousPeriodRange(normalizedPeriod, startLocal, endLocal);

        var start = ToOffset(startUtc);
        var end = ToOffset(endUtc);
        var prevStart = ToOffset(prevStartUtc);
        var prevEnd = ToOffset(prevEndUtc);
        var now = DateTimeOffset.UtcNow;

        var periodRows = await LoadInvolvedAsync(userId, start, end, cancellationToken);
        var previousRows = await LoadInvolvedAsync(userId, prevStart, prevEnd, cancellationToken);
        var openRows = await LoadOpenSnapshotAsync(userId, cancellationToken);

        var outgoing = periodRows.Where(x => x.InitiatorUserId == userId).ToList();
        var incoming = periodRows.Where(x => x.ExecutorUserId == userId).ToList();
        var prevOutgoing = previousRows.Where(x => x.InitiatorUserId == userId).ToList();
        var prevIncoming = previousRows.Where(x => x.ExecutorUserId == userId).ToList();

        var completedOutgoing = outgoing.Count(x => x.Status == ServiceRequestStatus.Completed);
        var completedIncoming = incoming.Count(x => x.Status == ServiceRequestStatus.Completed);
        var completedTotal = periodRows.Count(x => x.Status == ServiceRequestStatus.Completed);
        var prevCompletedTotal = previousRows.Count(x => x.Status == ServiceRequestStatus.Completed);

        var total = periodRows.Count;
        var prevTotal = previousRows.Count;
        // Доли по «ролям» (исх+вх), чтобы полоска сходилась в 100% даже при самоназначении.
        var roleTotal = outgoing.Count + incoming.Count;

        var reactionHours = incoming
            .Where(x => x.ReactionAt is not null)
            .Select(x => (decimal)(x.ReactionAt!.Value - x.SubmittedAt).TotalHours)
            .Where(h => h >= 0)
            .ToList();

        var completionHours = incoming
            .Where(x => x.Status == ServiceRequestStatus.Completed && x.CompletedAt is not null)
            .Select(x => (decimal)(x.CompletedAt!.Value - x.SubmittedAt).TotalHours)
            .Where(h => h >= 0)
            .ToList();

        // Как на календаре «Сегодня»: входящие без Returned; исходящие с Returned (надо переподать).
        var openOutgoing = openRows.Where(x => x.InitiatorUserId == userId && IsOpenOutgoing(x.Status)).ToList();
        var openIncoming = openRows.Where(x => x.ExecutorUserId == userId && IsOpenIncoming(x.Status)).ToList();
        var waitingAccept = openIncoming.Count(x => x.Status == ServiceRequestStatus.Submitted);
        // Ознакомление ≠ принятие: флаг Acknowledged ставится при открытии карточки исполнителем.
        var unacknowledged = openIncoming.Count(x => !x.Acknowledged);
        var returnedOutgoing = openOutgoing.Count(x => x.Status == ServiceRequestStatus.Returned);

        List<SummaryUserOptionResponse>? availableUsers = null;
        if (includeUserPicker)
        {
            // В выборе сводки — только инициаторы (как субъекты исходящих заявок).
            var initiators = await _users.GetUsersInRoleAsync(AppRoles.Department);
            availableUsers = initiators
                .Where(x => x.IsActive)
                .OrderBy(x => x.DisplayName)
                .ThenBy(x => x.UserName)
                .Select(x => new SummaryUserOptionResponse
                {
                    Id = x.Id,
                    UserName = x.UserName ?? string.Empty,
                    DisplayName = string.IsNullOrWhiteSpace(x.DisplayName)
                        ? (x.UserName ?? string.Empty)
                        : x.DisplayName!
                })
                .ToList();
        }

        return new SummaryResponse
        {
            Period = normalizedPeriod,
            PeriodStart = startLocal,
            PeriodEnd = endLocal,
            UserId = user.Id,
            UserDisplayName = string.IsNullOrWhiteSpace(user.DisplayName)
                ? (user.UserName ?? string.Empty)
                : user.DisplayName,
            AvailableUsers = availableUsers,
            OutgoingCount = outgoing.Count,
            IncomingCount = incoming.Count,
            TotalCount = total,
            CompletedOutgoingCount = completedOutgoing,
            CompletedIncomingCount = completedIncoming,
            CompletedTotalCount = completedTotal,
            CompletionRatePercent = Percent(completedTotal, total),
            OutgoingSharePercent = Percent(outgoing.Count, roleTotal),
            IncomingSharePercent = Percent(incoming.Count, roleTotal),
            AverageReactionHours = reactionHours.Count == 0
                ? null
                : Math.Round(reactionHours.Average(), 1),
            AverageCompletionHours = completionHours.Count == 0
                ? null
                : Math.Round(completionHours.Average(), 1),
            OpenOutgoingCount = openOutgoing.Count,
            OpenIncomingCount = openIncoming.Count,
            OverdueOutgoingCount = openOutgoing.Count(x => x.DueAt < now),
            OverdueIncomingCount = openIncoming.Count(x => x.DueAt < now),
            WaitingAcceptIncomingCount = waitingAccept,
            UnacknowledgedIncomingCount = unacknowledged,
            ReturnedOutgoingCount = returnedOutgoing,
            UrgentPeriodCount = periodRows.Count(x => x.IsUrgent),
            OpenUrgentCount = openOutgoing.Concat(openIncoming)
                .GroupBy(x => x.Id)
                .Count(g => g.First().IsUrgent),
            ComplaintsPeriodCount = periodRows.Count(x => x.HasComplaint),
            OutgoingStatusBreakdown = Breakdown(outgoing),
            IncomingStatusBreakdown = Breakdown(incoming),
            KindBreakdown = new SummaryKindBreakdownResponse
            {
                OtherCount = periodRows.Count(x => x.Kind == ServiceRequestKind.Other),
                EquipmentCount = periodRows.Count(x => x.Kind == ServiceRequestKind.Equipment),
                SpecTechCount = periodRows.Count(x => x.Kind == ServiceRequestKind.SpecTech)
            },
            PeriodComparison = new SummaryPeriodComparisonResponse
            {
                PreviousPeriodStart = prevStartLocal,
                PreviousPeriodEnd = prevEndLocal,
                PreviousOutgoingCount = prevOutgoing.Count,
                PreviousIncomingCount = prevIncoming.Count,
                PreviousTotalCount = prevTotal,
                PreviousCompletedTotalCount = prevCompletedTotal,
                PreviousCompletionRatePercent = Percent(prevCompletedTotal, prevTotal)
            },
            DailyTrend = BuildDailyTrend(periodRows, userId, normalizedPeriod, startLocal, endLocal)
        };
    }

    public static string NormalizePeriod(string? period) =>
        (period ?? "today").Trim().ToLowerInvariant() switch
        {
            "week" => "week",
            "month" => "month",
            "year" => "year",
            _ => "today"
        };

    /// <summary>Исходящие открыты, пока инициатору есть что делать (в т.ч. Returned).</summary>
    private static bool IsOpenOutgoing(ServiceRequestStatus status) =>
        status is ServiceRequestStatus.Submitted
            or ServiceRequestStatus.InProgress
            or ServiceRequestStatus.Returned;

    /// <summary>Входящие: как календарь — Returned исполнителю уже «закрыт» возвратом.</summary>
    private static bool IsOpenIncoming(ServiceRequestStatus status) =>
        status is ServiceRequestStatus.Submitted or ServiceRequestStatus.InProgress;

    private async Task<List<RequestSnap>> LoadInvolvedAsync(
        Guid userId,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        return await _db.ServiceRequests.AsNoTracking()
            .Where(x => x.SubmittedAt >= start && x.SubmittedAt < end)
            .Where(x => x.InitiatorUserId == userId || x.ExecutorUserId == userId)
            .Select(x => new RequestSnap(
                x.Id,
                x.InitiatorUserId,
                x.ExecutorUserId,
                x.Status,
                x.Kind,
                x.IsUrgent,
                x.SubmittedAt,
                x.DueAt,
                x.ReactionAt,
                x.CompletedAt,
                x.Acknowledged,
                x.ComplaintText != null && x.ComplaintText != ""))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<RequestSnap>> LoadOpenSnapshotAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _db.ServiceRequests.AsNoTracking()
            .Where(x => x.InitiatorUserId == userId || x.ExecutorUserId == userId)
            .Where(x => x.Status == ServiceRequestStatus.Submitted
                        || x.Status == ServiceRequestStatus.InProgress
                        || x.Status == ServiceRequestStatus.Returned)
            .Select(x => new RequestSnap(
                x.Id,
                x.InitiatorUserId,
                x.ExecutorUserId,
                x.Status,
                x.Kind,
                x.IsUrgent,
                x.SubmittedAt,
                x.DueAt,
                x.ReactionAt,
                x.CompletedAt,
                x.Acknowledged,
                x.ComplaintText != null && x.ComplaintText != ""))
            .ToListAsync(cancellationToken);
    }

    private List<SummaryDailyTrendItemResponse> BuildDailyTrend(
        List<RequestSnap> rows,
        Guid userId,
        string period,
        DateOnly startLocal,
        DateOnly endLocal)
    {
        var points = new List<SummaryDailyTrendItemResponse>();

        // Год — по месяцам, иначе колонок слишком много.
        if (period == "year")
        {
            for (var month = 1; month <= 12; month++)
            {
                var monthStart = new DateOnly(startLocal.Year, month, 1);
                if (monthStart > endLocal)
                {
                    break;
                }

                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                if (monthEnd > endLocal)
                {
                    monthEnd = endLocal;
                }

                if (monthEnd < startLocal)
                {
                    continue;
                }

                var rangeStart = monthStart < startLocal ? startLocal : monthStart;
                var (fromUtc, _) = _time.GetDayRangeUtc(rangeStart);
                var (_, toUtc) = _time.GetDayRangeUtc(monthEnd);
                var from = ToOffset(fromUtc);
                var to = ToOffset(toUtc);

                points.Add(new SummaryDailyTrendItemResponse
                {
                    Date = monthStart,
                    Label = monthStart.ToString("MM"),
                    SubmittedCount = rows.Count(x => x.SubmittedAt >= from && x.SubmittedAt < to),
                    CompletedCount = CountCompletedInRange(rows, userId, from, to)
                });
            }

            return points;
        }

        for (var day = startLocal; day <= endLocal; day = day.AddDays(1))
        {
            var (dayStartUtc, dayEndUtc) = _time.GetDayRangeUtc(day);
            var dayStart = ToOffset(dayStartUtc);
            var dayEnd = ToOffset(dayEndUtc);

            points.Add(new SummaryDailyTrendItemResponse
            {
                Date = day,
                Label = period == "month" ? day.Day.ToString() : day.ToString("dd.MM"),
                SubmittedCount = rows.Count(x => x.SubmittedAt >= dayStart && x.SubmittedAt < dayEnd),
                CompletedCount = CountCompletedInRange(rows, userId, dayStart, dayEnd)
            });
        }

        return points;
    }

    private static int CountCompletedInRange(
        List<RequestSnap> rows,
        Guid userId,
        DateTimeOffset from,
        DateTimeOffset to) =>
        rows.Count(x =>
            x.Status == ServiceRequestStatus.Completed
            && x.CompletedAt is { } completedAt
            && completedAt >= from
            && completedAt < to
            && (x.InitiatorUserId == userId || x.ExecutorUserId == userId));

    private static SummaryStatusBreakdownResponse Breakdown(IReadOnlyList<RequestSnap> rows) =>
        new()
        {
            SubmittedCount = rows.Count(x => x.Status == ServiceRequestStatus.Submitted),
            InProgressCount = rows.Count(x => x.Status == ServiceRequestStatus.InProgress),
            ReturnedCount = rows.Count(x => x.Status == ServiceRequestStatus.Returned),
            RejectedCount = rows.Count(x => x.Status == ServiceRequestStatus.Rejected),
            CompletedCount = rows.Count(x => x.Status == ServiceRequestStatus.Completed),
            CancelledCount = rows.Count(x => x.Status == ServiceRequestStatus.Cancelled)
        };

    private static decimal Percent(int part, int whole) =>
        whole <= 0 ? 0m : Math.Round((decimal)part / whole * 100m, 1);

    private static DateTimeOffset ToOffset(DateTime utc) =>
        new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    private (DateTime StartUtc, DateTime EndUtc, DateOnly StartLocal, DateOnly EndLocal) GetPeriodRange(
        string period,
        int? year,
        int? month)
    {
        var today = _time.GetToday();
        var (startLocal, endLocal) = ResolvePeriodBounds(period, today, year, month);
        var (startUtc, _) = _time.GetDayRangeUtc(startLocal);
        var (_, endUtc) = _time.GetDayRangeUtc(endLocal);
        return (startUtc, endUtc, startLocal, endLocal);
    }

    private (DateTime StartUtc, DateTime EndUtc, DateOnly StartLocal, DateOnly EndLocal) GetPreviousPeriodRange(
        string period,
        DateOnly currentStart,
        DateOnly currentEnd)
    {
        DateOnly startLocal;
        DateOnly endLocal;

        switch (period)
        {
            case "week":
                endLocal = currentStart.AddDays(-1);
                startLocal = endLocal.AddDays(-6);
                break;

            case "month":
                var monthStart = new DateOnly(currentStart.Year, currentStart.Month, 1).AddMonths(-1);
                startLocal = monthStart;
                endLocal = monthStart.AddMonths(1).AddDays(-1);
                break;

            case "year":
                var previousYear = currentStart.Year - 1;
                startLocal = new DateOnly(previousYear, 1, 1);
                endLocal = new DateOnly(previousYear, 12, 31);
                break;

            default:
                startLocal = currentStart.AddDays(-1);
                endLocal = startLocal;
                break;
        }

        var (startUtc, _) = _time.GetDayRangeUtc(startLocal);
        var (_, endUtc) = _time.GetDayRangeUtc(endLocal);
        return (startUtc, endUtc, startLocal, endLocal);
    }

    private static (DateOnly StartLocal, DateOnly EndLocal) ResolvePeriodBounds(
        string period,
        DateOnly today,
        int? year,
        int? month)
    {
        switch (period)
        {
            case "week":
                return (today.AddDays(-6), today);

            case "month":
                var monthYear = year ?? today.Year;
                var monthNumber = month ?? today.Month;
                if (monthNumber is < 1 or > 12)
                {
                    monthNumber = today.Month;
                }

                if (monthYear > today.Year || (monthYear == today.Year && monthNumber > today.Month))
                {
                    monthYear = today.Year;
                    monthNumber = today.Month;
                }

                var monthStart = new DateOnly(monthYear, monthNumber, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                if (monthEnd > today)
                {
                    monthEnd = today;
                }

                return (monthStart, monthEnd);

            case "year":
                var selectedYear = year ?? today.Year;
                if (selectedYear > today.Year)
                {
                    selectedYear = today.Year;
                }

                var yearStart = new DateOnly(selectedYear, 1, 1);
                var yearEnd = new DateOnly(selectedYear, 12, 31);
                if (yearEnd > today)
                {
                    yearEnd = today;
                }

                return (yearStart, yearEnd);

            default:
                return (today, today);
        }
    }

    private sealed record RequestSnap(
        long Id,
        Guid? InitiatorUserId,
        Guid? ExecutorUserId,
        ServiceRequestStatus Status,
        ServiceRequestKind Kind,
        bool IsUrgent,
        DateTimeOffset SubmittedAt,
        DateTimeOffset DueAt,
        DateTimeOffset? ReactionAt,
        DateTimeOffset? CompletedAt,
        bool Acknowledged,
        bool HasComplaint);
}
