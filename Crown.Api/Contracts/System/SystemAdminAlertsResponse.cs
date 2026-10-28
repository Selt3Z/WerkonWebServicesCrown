namespace Crown.Contracts.System;

public sealed class SystemAdminAlertsResponse
{
    /// <summary>Бэкап сегодня ещё не подтверждён (мягкий случай: день не закончен / ещё не гоняли).</summary>
    public bool HasBackupAlert { get; set; }

    /// <summary>Последний запуск бэкапа завершился с ошибкой.</summary>
    public bool HasBackupFailedAlert { get; set; }

    /// <summary>Давно не было успешного/любого запуска (нет активности сервиса).</summary>
    public bool HasBackupStaleAlert { get; set; }

    /// <summary>Есть неприменённые EF-миграции.</summary>
    public bool HasSchemaAlert { get; set; }

    /// <summary>Диск(и) ≥90% — зарезервировано (дорого считать на каждый page load).</summary>
    public bool HasDiskAlert { get; set; }

    public bool HasAnyAlert =>
        HasBackupAlert
        || HasBackupFailedAlert
        || HasBackupStaleAlert
        || HasSchemaAlert
        || HasDiskAlert;
}
