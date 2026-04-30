namespace AllWorkHRIS.Module.Payroll.Domain.Run;

public enum PayrollRunStatus
{
    Draft       = 1,
    Open        = 2,
    Calculating = 3,
    Calculated  = 4,
    UnderReview = 5,
    Approved    = 6,
    Releasing   = 7,
    Released    = 8,
    Closed      = 9,
    Failed      = 10,
    Cancelled   = 11
}
