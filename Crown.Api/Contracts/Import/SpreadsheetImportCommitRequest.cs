namespace Crown.Contracts.Import;

/// <summary>
/// Commit импорта каталога/склада. Индексы столбцов с 0.
/// Обязательность полей зависит от типа импорта (валидируется сервисом).
/// </summary>
public sealed class SpreadsheetImportCommitRequest
{
    public string FileBase64 { get; set; } = string.Empty;

    public int? NameColumn { get; set; }

    public int? CodeColumn { get; set; }

    public int? BarcodeColumn { get; set; }

    public int? CategoryColumn { get; set; }

    public int? UnitPriceColumn { get; set; }

    public int? UnitColumn { get; set; }

    public int? QuantityOnHandColumn { get; set; }

    public int? MinQuantityOnHandColumn { get; set; }

    public int? DescriptionColumn { get; set; }

    public int? IsArchivedColumn { get; set; }

    public int? IsActiveColumn { get; set; }

    /// <summary>Инв. № для импорта оборудования.</summary>
    public int? InventoryNumberColumn { get; set; }

    /// <summary>Текущее место (label) для импорта оборудования.</summary>
    public int? LocationColumn { get; set; }

    /// <summary>Владелец каталога услуг (обязателен для import-catalog-parts).</summary>
    public Guid? OwnerUserId { get; set; }

    // ---------- Импорт остановок (индексы с 0) ----------

    public int? ReportDateColumn { get; set; }

    public int? BrigadeColumn { get; set; }

    public int? MasterColumn { get; set; }

    public int? StoppedByColumn { get; set; }

    public int? StoppedAtColumn { get; set; }

    public int? RestartedAtColumn { get; set; }

    public int? WorkColumn { get; set; }

    /// <summary>Пункты акта: многострочная ячейка (как в экспорте «Анализ Остановок»).</summary>
    public int? RemarksColumn { get; set; }

    // ---------- Импорт проверок / ППК (индексы с 0) ----------

    /// <summary>Вид: Supervising / Pdk / Pbiot / Ppk. Для ППК UI шлёт Ppk.</summary>
    public string? InspectionKind { get; set; }

    public int? OutcomeColumn { get; set; }

    public int? WellColumn { get; set; }

    public int? CheckerColumn { get; set; }

    public int? CheckerPositionColumn { get; set; }

    public int? StopPointColumn { get; set; }

    public int? HasExplanationColumn { get; set; }

    public int? FixDateColumn { get; set; }
}
