namespace Crown.Infrastructure.Identity;

/// <summary>Единое отображаемое имя пользователя (бригады — с номером).</summary>
public static class UserDisplay
{
    /// <summary>
    /// Имя + « №{номер}», если номер не пустой.
    /// Для подразделений/админов номер обычно null — имя без изменений.
    /// </summary>
    public static string Format(string? displayName, string? userName, string? brigadeNumber)
    {
        var name = string.IsNullOrWhiteSpace(displayName)
            ? (userName ?? string.Empty)
            : displayName.Trim();

        var number = brigadeNumber?.Trim();
        return string.IsNullOrEmpty(number) ? name : $"{name} №{number}";
    }

    public static string Format(ApplicationUser user) =>
        Format(user.DisplayName, user.UserName, user.BrigadeNumber);

    /// <summary>
    /// DisplayName (или UserName) + « №{BrigadeNumber}» только если isBrigade.
    /// Для формы редактирования передавай isBrigade: false вместе с сырым именем — или читай поля отдельно.
    /// </summary>
    public static string FormatUserDisplay(ApplicationUser user, bool isBrigade) =>
        Format(user.DisplayName, user.UserName, isBrigade ? user.BrigadeNumber : null);

    /// <summary>Для списков, где пользователь — бригада (или номер заполнен).</summary>
    public static string FormatBrigadeDisplay(ApplicationUser user) => Format(user);
}
