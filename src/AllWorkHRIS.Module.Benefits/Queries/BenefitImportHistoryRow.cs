namespace AllWorkHRIS.Module.Benefits.Queries;

/// <summary>
/// One committed benefit-election import (an import that created ≥ 1 election).
/// </summary>
public sealed record BenefitImportHistoryRow(
    string   FileName,
    DateTime ImportedAt,
    string   ImportedBy,
    int            TotalRows,
    int            AcceptedCount,
    int            RejectedCount,
    string         Status);
