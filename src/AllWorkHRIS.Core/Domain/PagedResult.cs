namespace AllWorkHRIS.Core.Domain;

public sealed record PagedResult<T>
{
    public IReadOnlyList<T> Items      { get; init; } = [];
    public int              TotalCount { get; init; }
    public int              Page       { get; init; }
    public int              PageSize   { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}
