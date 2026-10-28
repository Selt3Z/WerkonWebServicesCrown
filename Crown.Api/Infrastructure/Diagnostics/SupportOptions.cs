namespace Crown.Infrastructure.Diagnostics;

/// <summary>
/// Куда «Связь с разработчиком» шлёт диагностику.
/// Telegram: WebhookUrl = https://api.telegram.org/bot&lt;TOKEN&gt;/sendMessage + TelegramChatId.
/// Любой webhook: POST JSON { text, version, hostname, report }.
/// Если URL пуст — только отчёт в ответе (скачать/скопировать).
/// </summary>
public sealed class SupportOptions
{
    public const string SectionName = "Support";

    public string? WebhookUrl { get; set; }

    public string? TelegramChatId { get; set; }
}
