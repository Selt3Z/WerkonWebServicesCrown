namespace Crown.Domain.Entities;

/// <summary>Вид проверки бригады (хаб «Проверки» + отдельный вход ППК).</summary>
public enum BrigadeInspectionKind
{
    /// <summary>Надзорная проверка (предписание / замечание / остановка).</summary>
    Supervising = 1,

    /// <summary>ПДК.</summary>
    Pdk = 2,

    /// <summary>ПБиОТ.</summary>
    Pbiot = 3,

    /// <summary>Пусковая приёмная комиссия (поля как у Супервайзинга).</summary>
    Ppk = 4
}

/// <summary>Результат надзорной проверки / ППК (Supervising, Ppk).</summary>
public enum BrigadeInspectionOutcome
{
    Prescription = 1,
    Remark = 2,
    Stoppage = 3
}
