using System.Data;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Module.Benefits.Commands;
using AllWorkHRIS.Module.Benefits.Domain.Elections;
using AllWorkHRIS.Module.Benefits.Repositories;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.Benefits.Services;

public sealed class BenefitElectionService : IBenefitElectionService
{
    private readonly IBenefitElectionRepository       _electionRepository;
    private readonly IDeductionRepository             _deductionRepository;
    private readonly IConnectionFactory               _connectionFactory;
    private readonly IEventPublisher                  _eventPublisher;
    private readonly ILogger<BenefitElectionService>  _logger;

    public BenefitElectionService(
        IBenefitElectionRepository           electionRepository,
        IDeductionRepository                 deductionRepository,
        IConnectionFactory                   connectionFactory,
        IEventPublisher                      eventPublisher,
        ILogger<BenefitElectionService>      logger)
    {
        _electionRepository  = electionRepository;
        _deductionRepository = deductionRepository;
        _connectionFactory   = connectionFactory;
        _eventPublisher      = eventPublisher;
        _logger              = logger;
    }

    // Opens a connection with SERIALIZABLE isolation so that the overlap-check SELECT
    // and the INSERT are a single atomic predicate-locked unit.
    private sealed class SerializableScope : IUnitOfWork
    {
        public IDbConnection  Connection  { get; }
        public IDbTransaction Transaction { get; }

        public SerializableScope(IConnectionFactory factory)
        {
            Connection  = factory.CreateConnection(); // CreateConnection already opens the connection
            Transaction = Connection.BeginTransaction(IsolationLevel.Serializable);
        }

        public void Commit()  => Transaction.Commit();
        public void Rollback() => Transaction.Rollback();

        public void Dispose()
        {
            Transaction.Dispose();
            Connection.Dispose();
        }
    }

    public async Task<Guid> CreateElectionAsync(CreateElectionCommand command, CancellationToken ct = default)
    {
        var deduction = await _deductionRepository.GetByIdAsync(command.DeductionId, ct)
            ?? throw new InvalidOperationException($"Deduction {command.DeductionId} not found or inactive.");

        if (command.EmployeeAmount.HasValue && command.EmployeeAmount.Value < 0)
            throw new ArgumentException("Employee amount must be ≥ 0.");

        if (command.EmployerContributionAmount.HasValue && command.EmployerContributionAmount.Value < 0)
            throw new ArgumentException("Employer contribution amount must be ≥ 0.");

        if (command.EffectiveEndDate.HasValue && command.EffectiveEndDate.Value < command.EffectiveStartDate)
            throw new ArgumentException("Effective end date must be on or after effective start date.");

        using var uow = new SerializableScope(_connectionFactory);
        try
        {
            var hasOverlap = await _electionRepository.HasOverlapAsync(
                command.EmploymentId, command.DeductionId,
                command.EffectiveStartDate, command.EffectiveEndDate, uow, ct);

            if (hasOverlap)
            {
                _logger.LogWarning(
                    "Overlap rejected — employment={EmploymentId} deduction={DeductionCode} start={Start}",
                    command.EmploymentId, deduction.Code, command.EffectiveStartDate);

                throw new InvalidOperationException(
                    $"An active election for '{deduction.Code}' already covers this period.");
            }

            var election   = BenefitDeductionElection.Create(command, deduction.TaxTreatment, deduction.Code);
            var electionId = await _electionRepository.InsertAsync(election, uow);
            uow.Commit();
            return electionId;
        }
        catch { uow.Rollback(); throw; }
    }

    public async Task<Guid> ImportElectionAsync(CreateElectionCommand command, CancellationToken ct = default)
    {
        var deduction = await _deductionRepository.GetByIdAsync(command.DeductionId, ct)
            ?? throw new InvalidOperationException($"Deduction {command.DeductionId} not found or inactive.");

        if (command.EmployeeAmount.HasValue && command.EmployeeAmount.Value < 0)
            throw new ArgumentException("Employee amount must be ≥ 0.");

        if (command.EmployerContributionAmount.HasValue && command.EmployerContributionAmount.Value < 0)
            throw new ArgumentException("Employer contribution amount must be ≥ 0.");

        if (command.EffectiveEndDate.HasValue && command.EffectiveEndDate.Value < command.EffectiveStartDate)
            throw new ArgumentException("Effective end date must be on or after effective start date.");

        using var uow = new SerializableScope(_connectionFactory);
        try
        {
            var overlapping = await _electionRepository.GetOverlappingByDeductionAsync(
                command.EmploymentId, command.DeductionId,
                command.EffectiveStartDate, command.EffectiveEndDate, uow, ct);

            foreach (var prior in overlapping)
                await _electionRepository.UpdateStatusAsync(prior.ElectionId, ElectionStatus.Superseded, uow);

            var election   = BenefitDeductionElection.Create(command, deduction.TaxTreatment, deduction.Code);
            var electionId = await _electionRepository.InsertAsync(election, uow);
            uow.Commit();
            return electionId;
        }
        catch { uow.Rollback(); throw; }
    }

    public async Task<Guid> AmendElectionAsync(AmendElectionCommand command, CancellationToken ct = default)
    {
        var prior = await _electionRepository.GetByIdAsync(command.ElectionId, ct)
            ?? throw new InvalidOperationException($"Election {command.ElectionId} not found.");

        if (prior.Status is not (ElectionStatus.Active or ElectionStatus.Pending))
            throw new InvalidOperationException(
                $"Election {command.ElectionId} cannot be amended (status: {prior.Status}).");

        if (command.AmendmentDate <= prior.EffectiveStartDate)
            throw new ArgumentException(
                "Amendment date must be after the election's current effective start date.");

        var trimmedEndDate = command.AmendmentDate.AddDays(-1);

        using var uow = new SerializableScope(_connectionFactory);
        try
        {
            // Trim and supersede the prior within the SERIALIZABLE scope first so it
            // is excluded from the overlap check (status = SUPERSEDED).
            await _electionRepository.TrimEndDateAsync(prior.ElectionId, trimmedEndDate, uow);
            await _electionRepository.UpdateStatusAsync(prior.ElectionId, ElectionStatus.Superseded, uow);

            // Now check for any OTHER elections covering the amendment period.
            var hasOverlap = await _electionRepository.HasOverlapAsync(
                prior.EmploymentId, prior.DeductionId,
                command.AmendmentDate, prior.EffectiveEndDate, uow, ct);

            if (hasOverlap)
                throw new InvalidOperationException(
                    $"Another active election for '{prior.DeductionCode}' overlaps the amendment period.");

            var amendment = BenefitDeductionElection.CreateAmendment(prior, command);
            var newId     = await _electionRepository.InsertAsync(amendment, uow);
            uow.Commit();
            return newId;
        }
        catch { uow.Rollback(); throw; }
    }

    public async Task<Guid> CorrectElectionAsync(CorrectElectionCommand command, CancellationToken ct = default)
    {
        var prior = await _electionRepository.GetByIdAsync(command.ElectionId, ct)
            ?? throw new InvalidOperationException($"Election {command.ElectionId} not found.");

        if (command.EffectiveStartDate.HasValue
            && command.EffectiveEndDate.HasValue
            && command.EffectiveEndDate.Value < command.EffectiveStartDate.Value)
            throw new ArgumentException("Effective end date must be on or after effective start date.");

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _electionRepository.UpdateStatusAsync(prior.ElectionId, ElectionStatus.Superseded, uow);
            var correction = BenefitDeductionElection.CreateCorrection(prior, command);
            var newId      = await _electionRepository.InsertAsync(correction, uow);
            uow.Commit();
            return newId;
        }
        catch { uow.Rollback(); throw; }
    }

    public async Task<Guid> UpdateElectionAsync(UpdateElectionCommand command, CancellationToken ct = default)
    {
        if (command.EffectiveEndDate.HasValue && command.EffectiveEndDate.Value < command.EffectiveStartDate)
            throw new ArgumentException("Effective end date must be on or after effective start date.");

        var prior = await _electionRepository.GetByIdAsync(command.ElectionId, ct)
            ?? throw new InvalidOperationException($"Election {command.ElectionId} not found.");

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _electionRepository.UpdateStatusAsync(prior.ElectionId, ElectionStatus.Superseded, uow);
            var updated = BenefitDeductionElection.CreateRevision(prior, command);
            var newId   = await _electionRepository.InsertAsync(updated, uow);
            uow.Commit();
            return newId;
        }
        catch { uow.Rollback(); throw; }
    }

    public async Task TerminateElectionAsync(TerminateElectionCommand command, CancellationToken ct = default)
    {
        var election = await _electionRepository.GetByIdAsync(command.ElectionId, ct)
            ?? throw new InvalidOperationException($"Election {command.ElectionId} not found.");

        var today   = DateOnly.FromDateTime(DateTime.Today);
        var endDate = command.EffectiveEndDate
            ?? (election.EffectiveStartDate > today
                ? election.EffectiveStartDate   // PENDING: close on the day it would have opened
                : today);

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            if (command.SourceEventId.HasValue)
                await _electionRepository.TerminateWithEventAsync(
                    election.ElectionId, endDate, command.SourceEventId.Value, uow);
            else
                await _electionRepository.TerminateAsync(election.ElectionId, endDate, uow);

            uow.Commit();
        }
        catch { uow.Rollback(); throw; }
    }

    public async Task SuspendElectionAsync(Guid electionId, Guid sourceEventId, CancellationToken ct = default)
    {
        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _electionRepository.UpdateStatusWithEventAsync(electionId, ElectionStatus.Suspended, sourceEventId, uow);
            uow.Commit();
        }
        catch { uow.Rollback(); throw; }
    }

    public async Task ReinstateElectionAsync(Guid electionId, Guid sourceEventId, CancellationToken ct = default)
    {
        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _electionRepository.UpdateStatusWithEventAsync(electionId, ElectionStatus.Active, sourceEventId, uow);
            uow.Commit();
        }
        catch { uow.Rollback(); throw; }
    }

    public async Task TerminateAllActiveAsync(Guid employmentId, Guid sourceEventId, CancellationToken ct = default)
    {
        var active = await _electionRepository.GetActiveByEmploymentIdAsync(
            employmentId, DateOnly.FromDateTime(DateTime.UtcNow), ct);

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            foreach (var e in active)
                await _electionRepository.UpdateStatusWithEventAsync(e.ElectionId, ElectionStatus.Terminated, sourceEventId, uow);
            uow.Commit();
        }
        catch { uow.Rollback(); throw; }
    }

    public async Task SuspendAllActiveAsync(Guid employmentId, Guid sourceEventId, CancellationToken ct = default)
    {
        var active = await _electionRepository.GetActiveByEmploymentIdAsync(
            employmentId, DateOnly.FromDateTime(DateTime.UtcNow), ct);

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            foreach (var e in active)
                await _electionRepository.UpdateStatusWithEventAsync(e.ElectionId, ElectionStatus.Suspended, sourceEventId, uow);
            uow.Commit();
        }
        catch { uow.Rollback(); throw; }
    }

    public async Task ReinstateAllSuspendedAsync(Guid employmentId, Guid sourceEventId, CancellationToken ct = default)
    {
        var suspended = await _electionRepository.GetSuspendedByEmploymentIdAsync(employmentId, ct);

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            foreach (var e in suspended)
                await _electionRepository.UpdateStatusWithEventAsync(e.ElectionId, ElectionStatus.Active, sourceEventId, uow);
            uow.Commit();
        }
        catch { uow.Rollback(); throw; }
    }

    public Task<IEnumerable<BenefitDeductionElection>> GetElectionsAsync(
        Guid employmentId, DateOnly asOf, CancellationToken ct = default)
        => _electionRepository.GetActiveByEmploymentIdAsync(employmentId, asOf, ct);
}
