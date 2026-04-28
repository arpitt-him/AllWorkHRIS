using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Host.Hris.Commands;
using AllWorkHRIS.Host.Hris.Domain;
using AllWorkHRIS.Host.Hris.Queries;
using AllWorkHRIS.Host.Hris.Repositories;

namespace AllWorkHRIS.Host.Hris.Services;

// ============================================================
// EXCEPTIONS
// ============================================================

public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

public sealed class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}

// ============================================================
// EMPLOYMENT SERVICE
// ============================================================

public interface IEmploymentService
{
    Task<HireResult>  HireEmployeeAsync(HireEmployeeCommand command);
    Task<HireResult>  RehireEmployeeAsync(RehireEmployeeCommand command, Guid personId);
    Task              TerminateEmployeeAsync(TerminateEmployeeCommand command);
    Task<Employment?> GetByIdAsync(Guid employmentId);
    Task<Employment?> GetActiveEmploymentAsync(Guid employmentId, DateOnly? asOf = null);
}

public sealed class EmploymentService : IEmploymentService
{
    private readonly IConnectionFactory       _connectionFactory;
    private readonly IPersonRepository        _personRepository;
    private readonly IEmploymentRepository    _employmentRepository;
    private readonly IAssignmentRepository    _assignmentRepository;
    private readonly ICompensationRepository  _compensationRepository;
    private readonly IEmployeeEventRepository _eventRepository;
    private readonly IEventPublisher          _eventPublisher;
    private readonly ITemporalContext         _temporalContext;

    public EmploymentService(
        IConnectionFactory       connectionFactory,
        IPersonRepository        personRepository,
        IEmploymentRepository    employmentRepository,
        IAssignmentRepository    assignmentRepository,
        ICompensationRepository  compensationRepository,
        IEmployeeEventRepository eventRepository,
        IEventPublisher          eventPublisher,
        ITemporalContext         temporalContext)
    {
        _connectionFactory      = connectionFactory;
        _personRepository       = personRepository;
        _employmentRepository   = employmentRepository;
        _assignmentRepository   = assignmentRepository;
        _compensationRepository = compensationRepository;
        _eventRepository        = eventRepository;
        _eventPublisher         = eventPublisher;
        _temporalContext        = temporalContext;
    }

    public async Task<HireResult> HireEmployeeAsync(HireEmployeeCommand command)
    {
        // 1. Validate command
        ValidateHireCommand(command);

        // 2. Check for duplicate employee number
        if (await _employmentRepository.ExistsWithNumberAsync(command.EmployeeNumber))
            throw new DomainException($"Employee number {command.EmployeeNumber} already exists.");

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            // 3. Create Person
            var person   = Person.CreateNew(command);
            var personId = await _personRepository.InsertAsync(person, uow);

            // 4. Create Employment
            var employment   = Employment.CreateFromHire(command, personId);
            var employmentId = await _employmentRepository.InsertAsync(employment, uow);

            // 5. Create initial Assignment
            var assignment = Assignment.CreateInitial(command, employmentId);
            await _assignmentRepository.InsertAsync(assignment, uow);

            // 6. Create initial Compensation Record
            var compensation = CompensationRecord.CreateInitial(command, employmentId);
            await _compensationRepository.InsertAsync(compensation, uow);

            // 7. Record lifecycle event
            var hireEvent = EmployeeEvent.CreateHire(personId, employmentId, command);
            var eventId   = await _eventRepository.InsertAsync(hireEvent, uow);

            // 8. Commit all writes atomically
            uow.Commit();

            // 9. Publish AFTER commit — never before
            await _eventPublisher.PublishAsync(new HireEventPayload
            {
                EmploymentId     = employmentId,
                PersonId         = personId,
                EventId          = eventId,
                TenantId         = Guid.Empty,
                EffectiveDate    = command.EmploymentStartDate,
                LegalEntityId    = command.LegalEntityId,
                FlsaStatus       = command.FlsaStatus,
                PayrollContextId = command.PayrollContextId,
                EventTimestamp   = DateTimeOffset.UtcNow
            });

            return new HireResult(personId, employmentId, eventId);
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public async Task<HireResult> RehireEmployeeAsync(RehireEmployeeCommand command, Guid personId)
    {
        var priorEmployments = await _employmentRepository.GetByPersonIdAsync(personId);
        var activeEmployment = priorEmployments.FirstOrDefault(e =>
            e.EmploymentStatus == EmploymentStatus.Active ||
            e.EmploymentStatus == EmploymentStatus.OnLeave);

        if (activeEmployment is not null)
            throw new DomainException("Cannot rehire — person has an active employment record.");

        var priorEmployment = priorEmployments
            .OrderByDescending(e => e.EmploymentStartDate)
            .FirstOrDefault();

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            var employment = new Employment
            {
                EmploymentId             = Guid.NewGuid(),
                PersonId                 = personId,
                LegalEntityId            = command.LegalEntityId,
                EmployerId               = command.LegalEntityId,
                EmployeeNumber           = command.EmployeeNumber,
                EmploymentType           = Enum.Parse<EmploymentType>(command.EmploymentType, ignoreCase: true),
                EmploymentStartDate      = command.EmploymentStartDate,
                OriginalHireDate         = priorEmployment?.OriginalHireDate ?? command.EmploymentStartDate,
                EmploymentStatus         = EmploymentStatus.Active,
                FullOrPartTimeStatus     = Enum.Parse<FullPartTimeStatus>(command.FullOrPartTimeStatus, ignoreCase: true),
                RegularOrTemporaryStatus = RegularTemporaryStatus.Regular,
                FlsaStatus               = Enum.Parse<FlsaStatus>(command.FlsaStatus, ignoreCase: true),
                PayrollContextId         = command.PayrollContextId,
                PrimaryWorkLocationId    = command.LocationId,
                PrimaryDepartmentId      = command.DepartmentId,
                ManagerEmploymentId      = command.ManagerEmploymentId,
                RehireFlag               = true,
                PriorEmploymentId        = priorEmployment?.EmploymentId,
                PrimaryFlag              = true,
                PayrollEligibilityFlag   = true,
                BenefitsEligibilityFlag  = true,
                TimeTrackingRequiredFlag = false,
                CreationTimestamp        = DateTimeOffset.UtcNow,
                LastUpdateTimestamp      = DateTimeOffset.UtcNow,
                LastUpdatedBy            = command.InitiatedBy.ToString()
            };

            var employmentId = await _employmentRepository.InsertAsync(employment, uow);

            var assignment = new Assignment
            {
                AssignmentId        = Guid.NewGuid(),
                EmploymentId        = employmentId,
                JobId               = command.JobId,
                PositionId          = command.PositionId,
                DepartmentId        = command.DepartmentId,
                LocationId          = command.LocationId,
                PayrollContextId    = command.PayrollContextId ?? Guid.Empty,
                AssignmentType      = AssignmentType.Primary,
                AssignmentStatus    = AssignmentStatus.Active,
                AssignmentStartDate = command.EmploymentStartDate,
                CreatedBy           = command.InitiatedBy,
                CreationTimestamp   = DateTimeOffset.UtcNow,
                LastUpdatedBy       = command.InitiatedBy,
                LastUpdateTimestamp = DateTimeOffset.UtcNow
            };

            await _assignmentRepository.InsertAsync(assignment, uow);

            var compensation = new CompensationRecord
            {
                CompensationId      = Guid.NewGuid(),
                EmploymentId        = employmentId,
                RateType            = Enum.Parse<CompensationRateType>(command.RateType, ignoreCase: true),
                BaseRate            = command.BaseRate,
                RateCurrency        = "USD",
                PayFrequency        = Enum.Parse<PayFrequency>(command.PayFrequency, ignoreCase: true),
                EffectiveStartDate  = command.EmploymentStartDate,
                CompensationStatus  = CompensationStatus.Active,
                ChangeReasonCode    = command.ChangeReasonCode,
                ApprovalStatus      = ApprovalStatus.Approved,
                PrimaryRateFlag     = true,
                CreatedBy           = command.InitiatedBy,
                CreationTimestamp   = DateTimeOffset.UtcNow,
                LastUpdatedBy       = command.InitiatedBy,
                LastUpdateTimestamp = DateTimeOffset.UtcNow
            };

            await _compensationRepository.InsertAsync(compensation, uow);

            var rehireEvent = new EmployeeEvent
            {
                EventId           = Guid.NewGuid(),
                EmploymentId      = employmentId,
                EventType         = EmployeeEventType.Rehire,
                EffectiveDate     = command.EmploymentStartDate,
                EventReason       = command.ChangeReasonCode,
                InitiatedBy       = command.InitiatedBy,
                CreationTimestamp = DateTimeOffset.UtcNow
            };

            var eventId = await _eventRepository.InsertAsync(rehireEvent, uow);

            uow.Commit();

            await _eventPublisher.PublishAsync(new RehireEventPayload
            {
                EmploymentId      = employmentId,
                PersonId          = personId,
                EventId           = eventId,
                TenantId          = Guid.Empty,
                EffectiveDate     = command.EmploymentStartDate,
                PriorEmploymentId = priorEmployment?.EmploymentId,
                PayrollContextId  = command.PayrollContextId,
                EventTimestamp    = DateTimeOffset.UtcNow
            });

            return new HireResult(personId, employmentId, eventId);
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public async Task TerminateEmployeeAsync(TerminateEmployeeCommand command)
    {
        var employment = await _employmentRepository.GetByIdAsync(command.EmploymentId)
            ?? throw new DomainException($"Employment {command.EmploymentId} not found.");

        if (employment.EmploymentStatus == EmploymentStatus.Terminated ||
            employment.EmploymentStatus == EmploymentStatus.Closed)
            throw new DomainException("Employment is already terminated.");

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _employmentRepository.UpdateStatusAsync(
                command.EmploymentId, "TERMINATED", command.TerminationDate, uow);

            var terminationEvent = EmployeeEvent.CreateTermination(command.EmploymentId, command);
            var eventId = await _eventRepository.InsertAsync(terminationEvent, uow);

            uow.Commit();

            await _eventPublisher.PublishAsync(new TerminationEventPayload
            {
                EmploymentId    = command.EmploymentId,
                PersonId        = employment.PersonId,
                EventId         = eventId,
                TenantId        = Guid.Empty,
                TerminationDate = command.TerminationDate,
                EventType       = command.EventType,
                ReasonCode      = command.ReasonCode,
                EventTimestamp  = DateTimeOffset.UtcNow
            });
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public async Task<Employment?> GetByIdAsync(Guid employmentId)
        => await _employmentRepository.GetByIdAsync(employmentId);

    public async Task<Employment?> GetActiveEmploymentAsync(Guid employmentId, DateOnly? asOf = null)
    {
        var effectiveDate = asOf ?? DateOnly.FromDateTime(_temporalContext.GetOperativeDate());
        var employment = await _employmentRepository.GetByIdAsync(employmentId);

        if (employment is null) return null;
        if (employment.EmploymentStartDate > effectiveDate) return null;
        if (employment.EmploymentEndDate.HasValue && employment.EmploymentEndDate < effectiveDate) return null;
        if (employment.EmploymentStatus == EmploymentStatus.Terminated ||
            employment.EmploymentStatus == EmploymentStatus.Closed) return null;

        return employment;
    }

    private static void ValidateHireCommand(HireEmployeeCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.LegalFirstName))
            throw new ValidationException("Legal first name is required.");
        if (string.IsNullOrWhiteSpace(command.LegalLastName))
            throw new ValidationException("Legal last name is required.");
        if (string.IsNullOrWhiteSpace(command.EmployeeNumber))
            throw new ValidationException("Employee number is required.");
        if (command.LegalEntityId == Guid.Empty)
            throw new ValidationException("Legal entity is required.");
        if (command.JobId == Guid.Empty)
            throw new ValidationException("Job is required.");
        if (command.DepartmentId == Guid.Empty)
            throw new ValidationException("Department is required.");
        if (command.LocationId == Guid.Empty)
            throw new ValidationException("Location is required.");
        if (command.BaseRate <= 0)
            throw new ValidationException("Base rate must be greater than zero.");
    }
}

// ============================================================
// PERSON SERVICE
// ============================================================

public interface IPersonService
{
    Task<Person?>  GetByIdAsync(Guid personId);
    Task<Person?>  GetByEmploymentIdAsync(Guid employmentId);
    Task           UpdatePersonAsync(UpdatePersonCommand command);
}

public sealed class PersonService : IPersonService
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly IPersonRepository  _personRepository;

    public PersonService(
        IConnectionFactory connectionFactory,
        IPersonRepository  personRepository)
    {
        _connectionFactory = connectionFactory;
        _personRepository  = personRepository;
    }

    public async Task<Person?> GetByIdAsync(Guid personId)
        => await _personRepository.GetByIdAsync(personId);

    public async Task<Person?> GetByEmploymentIdAsync(Guid employmentId)
        => await _personRepository.GetByEmploymentIdAsync(employmentId);

    public async Task UpdatePersonAsync(UpdatePersonCommand command)
    {
        var person = await _personRepository.GetByIdAsync(command.PersonId)
            ?? throw new DomainException($"Person {command.PersonId} not found.");

        var updated = person with
        {
            PreferredName       = command.PreferredName      ?? person.PreferredName,
            Gender              = command.Gender             ?? person.Gender,
            Pronouns            = command.Pronouns           ?? person.Pronouns,
            MaritalStatus       = command.MaritalStatus      ?? person.MaritalStatus,
            LanguagePreference  = command.LanguagePreference ?? person.LanguagePreference,
            VeteranStatus       = command.VeteranStatus      ?? person.VeteranStatus,
            DisabilityStatus    = command.DisabilityStatus   ?? person.DisabilityStatus,
            LastUpdateTimestamp = DateTimeOffset.UtcNow,
            LastUpdatedBy       = command.InitiatedBy.ToString()
        };

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _personRepository.UpdateAsync(updated, uow);
            uow.Commit();
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }
}

// ============================================================
// COMPENSATION SERVICE
// ============================================================

public interface ICompensationService
{
    Task<Guid>                            ChangeCompensationAsync(ChangeCompensationCommand command);
    Task<CompensationRecord?>             GetCurrentRateAsync(Guid employmentId, DateOnly asOf);
    Task<IEnumerable<CompensationRecord>> GetRateHistoryAsync(Guid employmentId);
}

public sealed class CompensationService : ICompensationService
{
    private readonly IConnectionFactory       _connectionFactory;
    private readonly ICompensationRepository  _compensationRepository;
    private readonly IEmployeeEventRepository _eventRepository;
    private readonly IEventPublisher          _eventPublisher;
    private readonly ITemporalContext         _temporalContext;

    public CompensationService(
        IConnectionFactory       connectionFactory,
        ICompensationRepository  compensationRepository,
        IEmployeeEventRepository eventRepository,
        IEventPublisher          eventPublisher,
        ITemporalContext         temporalContext)
    {
        _connectionFactory      = connectionFactory;
        _compensationRepository = compensationRepository;
        _eventRepository        = eventRepository;
        _eventPublisher         = eventPublisher;
        _temporalContext        = temporalContext;
    }

    public async Task<Guid> ChangeCompensationAsync(ChangeCompensationCommand command)
    {
        var operativeDate = DateOnly.FromDateTime(_temporalContext.GetOperativeDate());
        var isRetroactive = command.EffectiveDate < operativeDate;

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _compensationRepository.CloseCurrentAsync(
                command.EmploymentId, command.EffectiveDate.AddDays(-1), uow);

            var newRecord = new CompensationRecord
            {
                CompensationId      = Guid.NewGuid(),
                EmploymentId        = command.EmploymentId,
                RateType            = Enum.Parse<CompensationRateType>(command.RateType, ignoreCase: true),
                BaseRate            = command.NewBaseRate,
                RateCurrency        = "USD",
                PayFrequency        = Enum.Parse<PayFrequency>(command.PayFrequency, ignoreCase: true),
                EffectiveStartDate  = command.EffectiveDate,
                CompensationStatus  = CompensationStatus.Active,
                ChangeReasonCode    = command.ChangeReasonCode,
                ApprovalStatus      = ApprovalStatus.Approved,
                PrimaryRateFlag     = true,
                CreatedBy           = command.InitiatedBy,
                CreationTimestamp   = DateTimeOffset.UtcNow,
                LastUpdatedBy       = command.InitiatedBy,
                LastUpdateTimestamp = DateTimeOffset.UtcNow
            };

            await _compensationRepository.InsertAsync(newRecord, uow);

            var compEvent = EmployeeEvent.CreateCompensationChange(command.EmploymentId, command);
            var eventId   = await _eventRepository.InsertAsync(compEvent, uow);

            uow.Commit();

            await _eventPublisher.PublishAsync(new CompensationChangeEventPayload
            {
                EmploymentId   = command.EmploymentId,
                PersonId       = Guid.Empty,
                EventId        = eventId,
                TenantId       = Guid.Empty,
                EffectiveDate  = command.EffectiveDate,
                RateType       = command.RateType,
                NewBaseRate    = command.NewBaseRate,
                PayFrequency   = command.PayFrequency,
                IsRetroactive  = isRetroactive,
                EventTimestamp = DateTimeOffset.UtcNow
            });

            return eventId;
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public async Task<CompensationRecord?> GetCurrentRateAsync(Guid employmentId, DateOnly asOf)
        => await _compensationRepository.GetActiveByEmploymentIdAsync(employmentId, asOf);

    public async Task<IEnumerable<CompensationRecord>> GetRateHistoryAsync(Guid employmentId)
        => await _compensationRepository.GetHistoryByEmploymentIdAsync(employmentId);
}

// ============================================================
// LIFECYCLE EVENT SERVICE
// ============================================================

public interface ILifecycleEventService
{
    Task<EmployeeEvent>  InitiateEventAsync(Guid employmentId, string eventType,
                             string reasonCode, Guid initiatedBy);
    Task                 ApproveEventAsync(Guid eventId, Guid approvedBy);
    Task                 RejectEventAsync(Guid eventId, Guid rejectedBy, string reason);
    Task<EmployeeEvent?> GetPendingEventAsync(Guid employmentId, string eventType);
}

public sealed class LifecycleEventService : ILifecycleEventService
{
    private readonly IConnectionFactory       _connectionFactory;
    private readonly IEmployeeEventRepository _eventRepository;

    public LifecycleEventService(
        IConnectionFactory       connectionFactory,
        IEmployeeEventRepository eventRepository)
    {
        _connectionFactory = connectionFactory;
        _eventRepository   = eventRepository;
    }

    public async Task<EmployeeEvent> InitiateEventAsync(Guid employmentId, string eventType,
        string reasonCode, Guid initiatedBy)
    {
        var employeeEvent = new EmployeeEvent
        {
            EventId           = Guid.NewGuid(),
            EmploymentId      = employmentId,
            EventType         = Enum.Parse<EmployeeEventType>(eventType, ignoreCase: true),
            EffectiveDate     = DateOnly.FromDateTime(DateTime.UtcNow),
            EventReason       = reasonCode,
            InitiatedBy       = initiatedBy,
            CreationTimestamp = DateTimeOffset.UtcNow
        };

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _eventRepository.InsertAsync(employeeEvent, uow);
            uow.Commit();
        }
        catch
        {
            uow.Rollback();
            throw;
        }

        return employeeEvent;
    }

    public Task ApproveEventAsync(Guid eventId, Guid approvedBy)
        => throw new NotImplementedException("Workflow approval implemented in Phase 3.");

    public Task RejectEventAsync(Guid eventId, Guid rejectedBy, string reason)
        => throw new NotImplementedException("Workflow rejection implemented in Phase 3.");

    public async Task<EmployeeEvent?> GetPendingEventAsync(Guid employmentId, string eventType)
    {
        var events = await _eventRepository.GetByEmploymentIdAsync(employmentId);
        var parsedType = Enum.Parse<EmployeeEventType>(eventType, ignoreCase: true);
        return events.FirstOrDefault(e => e.EventType == parsedType);
    }
}

// ============================================================
// ORG STRUCTURE SERVICE
// ============================================================

public interface IOrgStructureService
{
    Task<OrgUnit?>             GetOrgUnitByIdAsync(Guid orgUnitId);
    Task<IEnumerable<OrgUnit>> GetLegalEntitiesAsync();
    Task<IEnumerable<OrgUnit>> GetDepartmentsAsync();
    Task<IEnumerable<OrgUnit>> GetLocationsAsync();
    Task<IEnumerable<OrgUnit>> GetChildrenAsync(Guid parentOrgUnitId);
    Task<IEnumerable<OrgUnit>> GetAllActiveAsync();
}

public sealed class OrgStructureService : IOrgStructureService
{
    private readonly IOrgUnitRepository _orgUnitRepository;

    public OrgStructureService(IOrgUnitRepository orgUnitRepository)
        => _orgUnitRepository = orgUnitRepository;

    public async Task<OrgUnit?> GetOrgUnitByIdAsync(Guid orgUnitId)
        => await _orgUnitRepository.GetByIdAsync(orgUnitId);

    public async Task<IEnumerable<OrgUnit>> GetLegalEntitiesAsync()
        => await _orgUnitRepository.GetByTypeAsync(OrgUnitType.LegalEntity);

    public async Task<IEnumerable<OrgUnit>> GetDepartmentsAsync()
        => await _orgUnitRepository.GetByTypeAsync(OrgUnitType.Department);

    public async Task<IEnumerable<OrgUnit>> GetLocationsAsync()
        => await _orgUnitRepository.GetByTypeAsync(OrgUnitType.Location);

    public async Task<IEnumerable<OrgUnit>> GetChildrenAsync(Guid parentOrgUnitId)
        => await _orgUnitRepository.GetChildrenAsync(parentOrgUnitId);

    public async Task<IEnumerable<OrgUnit>> GetAllActiveAsync()
        => await _orgUnitRepository.GetAllActiveAsync();
}
