using System.Text.Json;

namespace Crown.Web.Services;

/// <summary>
/// Разбор тела ошибки API и перевод кодов на русский.
/// API отдаёт либо { code, codes, message } (ApiErrorResponse), либо { error, message } (RequestsController),
/// либо стандартный ProblemDetails.
/// </summary>
public static class ApiErrorTranslator
{
    private static readonly Dictionary<string, string> Messages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["invalid_credentials"] = "Неверный логин или пароль.",
        ["account_disabled"] = "Учётная запись отключена.",
        ["account_locked_out"] = "Учётная запись временно заблокирована из-за неудачных попыток входа.",
        ["login_credentials_required"] = "Введите логин и пароль.",

        ["phone_required"] = "Телефон обязателен.",
        ["body_required"] = "Текст заявки обязателен.",
        ["initiator_required"] = "Инициатор обязателен.",
        ["department_invalid"] = "Служба не найдена.",
        ["due_required"] = "Укажите новый срок.",
        ["comment_required"] = "Для переноса срока нужен комментарий.",
        ["action_unknown"] = "Неизвестное действие.",
        ["concurrency"] = "Заявка изменена другим пользователем. Обновите страницу.",
        ["file_required"] = "Файл не передан.",
        ["file_too_large"] = "Файл слишком большой.",

        ["user_name_required"] = "Укажите логин.",
        ["display_name_required"] = "Укажите отображаемое имя.",
        ["role_required"] = "Выберите роль.",
        ["role_not_found"] = "Роль не найдена.",
        ["password_required"] = "Укажите пароль.",
        ["current_password_required"] = "Укажите текущий пароль.",
        ["current_password_invalid"] = "Текущий пароль указан неверно.",
        ["password_too_short"] = "Пароль слишком короткий (минимум 8 символов).",
        ["password_requires_digit"] = "В пароле нужна хотя бы одна цифра.",
        ["password_requires_lower"] = "В пароле нужна хотя бы одна строчная буква.",
        ["password_requires_upper"] = "В пароле нужна хотя бы одна заглавная буква.",
        ["password_requires_non_alphanumeric"] = "В пароле нужен хотя бы один спецсимвол.",
        ["duplicate_user_name"] = "Пользователь с таким логином уже существует.",
        ["invalid_user_name"] = "Недопустимый логин.",
        ["cannot_deactivate_own_account"] = "Нельзя отключить собственную учётную запись.",
        ["cannot_delete_own_account"] = "Нельзя удалить собственную учётную запись.",
        ["protected_admin_login_immutable"] = "Логин главного администратора менять нельзя.",
        ["protected_admin_role_immutable"] = "Роль главного администратора менять нельзя.",
        ["protected_admin_cannot_delete"] = "Главного администратора удалить нельзя.",

        ["unsupported_currency"] = "Валюта не поддерживается.",
        ["invalid_time_zone"] = "Неизвестный часовой пояс.",
        ["warehouse_disabled"] = "Складской учёт отключён в настройках.",
        ["backup_already_running"] = "Резервное копирование уже выполняется.",
        ["backup_failed"] = "Не удалось создать резервную копию.",
        ["internal_server_error"] = "Внутренняя ошибка сервера. Возможно, действие недоступно в текущем состоянии."
    };

    public static string Translate(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Messages.TryGetValue(code, out var text) ? text : string.Empty;

    public static string DefaultForStatus(int status) => status switch
    {
        0 => "Сервер API недоступен. Повторите попытку позже.",
        400 => "Некорректный запрос.",
        401 => "Сессия истекла. Войдите заново.",
        403 => "Недостаточно прав для этого действия.",
        404 => "Не найдено.",
        409 => "Конфликт данных. Обновите страницу и повторите.",
        423 => "Учётная запись заблокирована.",
        429 => "Слишком много запросов. Подождите минуту.",
        >= 500 => "Ошибка сервера. Повторите попытку позже.",
        _ => $"Ошибка запроса ({status})."
    };

    public static string FromBody(int status, string? body)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var root = doc.RootElement;

                    // 1) коды -> перевод.
                    var translated = new List<string>();
                    if (root.TryGetProperty("codes", out var codes) && codes.ValueKind == JsonValueKind.Array)
                    {
                        translated.AddRange(codes.EnumerateArray()
                            .Select(c => Translate(c.GetString()))
                            .Where(t => t.Length > 0));
                    }

                    foreach (var name in new[] { "code", "error", "errorCode" })
                    {
                        if (translated.Count == 0
                            && root.TryGetProperty(name, out var single)
                            && single.ValueKind == JsonValueKind.String)
                        {
                            var t = Translate(single.GetString());
                            if (t.Length > 0)
                            {
                                translated.Add(t);
                            }
                        }
                    }

                    if (translated.Count > 0)
                    {
                        return string.Join(" ", translated.Distinct());
                    }

                    // 2) готовое сообщение (RequestsController отдаёт русский текст).
                    if (root.TryGetProperty("message", out var message)
                        && message.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(message.GetString()))
                    {
                        return message.GetString()!;
                    }

                    // 3) ProblemDetails от model validation.
                    if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                    {
                        var first = errors.EnumerateObject()
                            .SelectMany(p => p.Value.ValueKind == JsonValueKind.Array
                                ? p.Value.EnumerateArray().Select(v => v.GetString())
                                : [])
                            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                        if (first is not null)
                        {
                            return first;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Тело не JSON — используем текст по статусу.
            }
        }

        return DefaultForStatus(status);
    }
}
