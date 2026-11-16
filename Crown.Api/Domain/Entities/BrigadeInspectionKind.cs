namespace Crown.Domain.Entities;

/// <summary>Вид проверки бригады (три вкладки/таблицы «Проверки»).</summary>
public enum BrigadeInspectionKind
{
    /// <summary>Надзорная проверка (предписание / замечание / остановка).</summary>
    Supervising = 1,

    /// <summary>ПДК.</summary>
    Pdk = 2,

    /// <summary>ПБиОТ.</summary>
    Pbiot = 3
}

/// <summary>Результат надзорной проверки (только для Supervising).</summary>
public enum BrigadeInspectionOutcome
{
    Prescription = 1,
    Remark = 2,
    Stoppage = 3
}
