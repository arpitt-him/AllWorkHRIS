namespace AllWorkHRIS.Core.Lookups;

public sealed record LookupEntry
{
    public int    Id    { get; init; }
    public string Code  { get; init; } = default!;
    public string Label { get; init; } = default!;
}
