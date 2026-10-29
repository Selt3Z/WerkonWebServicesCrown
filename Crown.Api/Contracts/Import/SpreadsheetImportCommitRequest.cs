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

    /// <summary>Владелец каталога услуг (обязателен для import-catalog-parts).</summary>
    public Guid? OwnerUserId { get; set; }
}
