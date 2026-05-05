using AllWorkHRIS.Module.Benefits.Steps.Calculators;

namespace AllWorkHRIS.Module.Benefits.Steps;

public sealed class BenefitCalculatorFactory
{
    private readonly IReadOnlyDictionary<string, IBenefitCalculator> _calculators;

    public BenefitCalculatorFactory(IEnumerable<IBenefitCalculator> calculators)
        => _calculators = calculators.ToDictionary(c => c.Mode, StringComparer.OrdinalIgnoreCase);

    public IBenefitCalculator GetCalculator(string mode)
        => _calculators.TryGetValue(mode, out var calc) ? calc
           : throw new NotSupportedException($"No benefit calculator registered for mode '{mode}'.");
}
