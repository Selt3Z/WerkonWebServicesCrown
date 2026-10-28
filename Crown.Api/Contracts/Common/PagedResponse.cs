namespace Crown.Contracts.Common;

public sealed class PagedResponse<T>
{
    public List<T> Items { get; set; } = [];
    public bool HasMore { get; set; }
}
