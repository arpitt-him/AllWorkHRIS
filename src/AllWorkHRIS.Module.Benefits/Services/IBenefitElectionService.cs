using AllWorkHRIS.Module.Benefits.Commands;
using AllWorkHRIS.Module.Benefits.Domain.Elections;

namespace AllWorkHRIS.Module.Benefits.Services;

public interface IBenefitElectionService
{
    Task<Guid> CreateElectionAsync(CreateElectionCommand command, CancellationToken ct = default);

    // Import variant: supersedes any overlapping elections before creating the new one.
    Task<Guid> ImportElectionAsync(CreateElectionCommand command, CancellationToken ct = default);

    // Prospective forward change: trims the prior election to AmendmentDate - 1, inserts a new election from AmendmentDate.
    // Runs under SERIALIZABLE isolation to prevent concurrent overlap.
    Task<Guid> AmendElectionAsync(AmendElectionCommand command, CancellationToken ct = default);

    // Retroactive data correction: supersedes the prior election and inserts a replacement with corrected data.
    Task<Guid> CorrectElectionAsync(CorrectElectionCommand command, CancellationToken ct = default);

    Task<Guid> UpdateElectionAsync(UpdateElectionCommand command, CancellationToken ct = default);
    Task       TerminateElectionAsync(TerminateElectionCommand command, CancellationToken ct = default);
    Task       SuspendElectionAsync(Guid electionId, Guid sourceEventId, CancellationToken ct = default);
    Task       ReinstateElectionAsync(Guid electionId, Guid sourceEventId, CancellationToken ct = default);

    Task       TerminateAllActiveAsync(Guid employmentId, Guid sourceEventId, CancellationToken ct = default);
    Task       SuspendAllActiveAsync(Guid employmentId, Guid sourceEventId, CancellationToken ct = default);
    Task       ReinstateAllSuspendedAsync(Guid employmentId, Guid sourceEventId, CancellationToken ct = default);

    Task<IEnumerable<BenefitDeductionElection>> GetElectionsAsync(Guid employmentId, DateOnly asOf, CancellationToken ct = default);
}
