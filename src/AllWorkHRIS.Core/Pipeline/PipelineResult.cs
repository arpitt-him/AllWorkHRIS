namespace AllWorkHRIS.Core.Pipeline;

public sealed record PipelineResult
{
    public required bool    Succeeded      { get; init; }
    public string?          FailureReason  { get; init; }
    public required decimal GrossPayPeriod { get; init; }
    public required decimal NetPay         { get; init; }
    public required decimal ComputedTax    { get; init; }
    public required decimal EmployerCost   { get; init; }

    // Employee tax step amounts keyed by step_code — excludes employer contributions
    public IReadOnlyDictionary<string, decimal> StepResults { get; init; }
        = new Dictionary<string, decimal>();

    // Employer contribution amounts keyed by step_code (social insurance employer share, etc.)
    public IReadOnlyDictionary<string, decimal> EmployerStepResults { get; init; }
        = new Dictionary<string, decimal>();
}
