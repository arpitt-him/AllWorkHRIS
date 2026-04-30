using AllWorkHRIS.Module.Payroll.Domain.Results;

namespace AllWorkHRIS.Module.Payroll.Repositories;

public interface IResultLineRepository
{
    Task InsertEarningsLineAsync(EarningsResultLine line);
    Task InsertDeductionLineAsync(DeductionResultLine line);
    Task InsertTaxLineAsync(TaxResultLine line);
    Task InsertEmployerContributionLineAsync(EmployerContributionResultLine line);

    Task<IReadOnlyList<EarningsResultLine>>             GetEarningsByResultIdAsync(Guid employeePayrollResultId);
    Task<IReadOnlyList<DeductionResultLine>>            GetDeductionsByResultIdAsync(Guid employeePayrollResultId);
    Task<IReadOnlyList<TaxResultLine>>                  GetTaxLinesByResultIdAsync(Guid employeePayrollResultId);
    Task<IReadOnlyList<EmployerContributionResultLine>> GetContributionsByResultIdAsync(Guid employeePayrollResultId);
}
