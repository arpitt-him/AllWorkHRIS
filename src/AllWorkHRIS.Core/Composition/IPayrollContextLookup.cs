namespace AllWorkHRIS.Core.Composition;

/// <summary>
/// Thin discovery interface placed in Core so HRIS can ask "are there any payroll contexts?"
/// without taking a direct dependency on the Payroll module.
/// Implemented by the Payroll module; absent if the module is not loaded.
/// </summary>
public interface IPayrollContextLookup
{
    Task<IReadOnlyList<(Guid Id, string Name)>> GetActiveContextsAsync();
    Task<IReadOnlyList<(Guid Id, string Name)>> GetActiveContextsByLegalEntityAsync(Guid legalEntityId);
}
