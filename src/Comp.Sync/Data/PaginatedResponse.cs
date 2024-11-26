namespace Comp.Sync.Data;
public class PaginatedResponse<T>
{
    public IEnumerable<T> Items { get; init; } = [];
    public int Offset { get; init; }
    public int Limit { get; init; }
    public int TotalCount { get; init; }
}