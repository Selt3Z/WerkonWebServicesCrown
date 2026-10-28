namespace Crown.Domain;

/// <summary>Статусы заявки — как в черновике заказчика.</summary>
public enum ServiceRequestStatus
{
    Submitted = 1,
    InProgress = 2,
    Rejected = 3,
    Returned = 4,
    Completed = 5,
    Cancelled = 6
}
