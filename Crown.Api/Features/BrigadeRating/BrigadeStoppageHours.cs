namespace Crown.Features.BrigadeRating;

internal static class BrigadeStoppageHours
{
    /// <summary>Часы между stop и restart; если restart раньше stop — считаем через полночь.</summary>
    public static decimal Compute(TimeOnly stoppedAt, TimeOnly? restartedAt)
    {
        if (restartedAt is null)
        {
            return 0m;
        }

        var start = stoppedAt.ToTimeSpan();
        var end = restartedAt.Value.ToTimeSpan();
        if (end < start)
        {
            end = end.Add(TimeSpan.FromDays(1));
        }

        return Math.Round((decimal)(end - start).TotalHours, 2);
    }
}
