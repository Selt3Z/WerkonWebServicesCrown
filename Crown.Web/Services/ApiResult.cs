namespace Crown.Web.Services;

/// <summary>Маркер для вызовов API без тела ответа.</summary>
public sealed class NoContent;

public class ApiResult
{
    public bool Ok { get; init; }

    /// <summary>HTTP-статус API; 0 — API недоступен.</summary>
    public int Status { get; init; }

    /// <summary>Сообщение об ошибке, уже по-русски.</summary>
    public string? Error { get; init; }

    public bool IsUnauthorized => Status == 401;
}

public sealed class ApiResult<T> : ApiResult
{
    public T? Data { get; init; }

    public static ApiResult<T> Success(T? data, int status = 200) =>
        new() { Ok = true, Status = status, Data = data };

    public static ApiResult<T> Failure(int status, string error) =>
        new() { Ok = false, Status = status, Error = error };
}
