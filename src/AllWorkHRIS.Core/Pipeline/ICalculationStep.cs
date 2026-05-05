namespace AllWorkHRIS.Core.Pipeline;

public interface ICalculationStep
{
    string        StepCode       { get; }
    int           SequenceNumber { get; }
    StepAppliesTo AppliesTo      { get; }

    Task<CalculationContext> ExecuteAsync(CalculationContext ctx, CancellationToken ct = default);
}
