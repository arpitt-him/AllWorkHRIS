namespace AllWorkHRIS.Module.Payroll.Domain.Accumulators;

public sealed record AccumulatorDefinition
{
    public Guid        AccumulatorDefinitionId { get; init; }
    public int         AccumulatorFamilyId     { get; init; }
    public string      AccumulatorCode         { get; init; } = default!;
    public string      AccumulatorName         { get; init; } = default!;
    public int         ScopeTypeId             { get; init; }
    public int         PeriodContextId         { get; init; }
    public string      ResetType               { get; init; } = default!;
    public bool        CarryForwardFlag        { get; init; }
    public bool        ReportingFlag           { get; init; }
    public bool        RemittanceFlag          { get; init; }
    public DateOnly    EffectiveStartDate      { get; init; }
    public DateOnly?   EffectiveEndDate        { get; init; }
    public Guid        CreatedBy               { get; init; }
    public DateTimeOffset CreationTimestamp    { get; init; }
    public Guid        LastUpdatedBy           { get; init; }
    public DateTimeOffset LastUpdateTimestamp  { get; init; }
}
