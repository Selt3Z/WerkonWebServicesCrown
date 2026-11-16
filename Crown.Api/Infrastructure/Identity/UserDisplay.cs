namespace Crown.Infrastructure.Identity;

/// <summary>Единое отображаемое имя пользователя (бригады — с номером).</summary>
public static class UserDisplay
{
    /// <summary>
    /// DisplayName (или UserName, если пусто) + « №{BrigadeNumber}» для роли Brigade с заполненным номером.
    /// Роль передаётся флагом: сам ApplicationUser ролей не хранит (они в Identity).
    /// </summary>
    public static string FormatUserDisplay(ApplicationUser user, bool isBrigade)
    {
        var name = string.IsNullOrWhiteSpace(user.DisplayName)
            ? (user.UserName ?? string.Empty)
            : user.DisplayName;

        if (!isBrigade)
        {
            return name;
        }

        var number = user.BrigadeNumber?.Trim();
        return string.IsNullOrEmpty(number) ? name : $"{name} №{number}";
    }

    /// <summary>Для списков, где все пользователи гарантированно в роли Brigade.</summary>
    public static string FormatBrigadeDisplay(ApplicationUser user) =>
        FormatUserDisplay(user, isBrigade: true);
}
