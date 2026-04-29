using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Core.Lookups;
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
    Task<PagedResult<EmploymentListItem>> GetPagedListAsync(EmployeeListQuery query);
    Task<EmployeeStatCards>               GetStatCardsAsync(DateOnly asOf);
}

public sealed class EmploymentService : IEmploymentService
{
    private readonly IConnectionFactory         _connectionFactory;
    private readonly IPersonRepository          _personRepository;
    private readonly IPersonAddressRepository   _personAddressRepository;
    private readonly IEmploymentRepository      _employmentRepository;
    private readonly IAssignmentRepository      _assignmentRepository;
    private readonly ICompensationRepository    _compensationRepository;
    private readonly IEmployeeEventRepository   _eventRepository;
    private readonly IEventPublisher            _eventPublisher;
    private readonly ITemporalContext           _temporalContext;
    private readonly ILookupCache               _lookupCache;

    private readonly int _activeStatusId;
    private readonly int _terminatedStatusId;
    private readonly int _closedStatusId;
    private readonly int _onLeaveStatusId;
    private readonly int _regularTempStatusId;
    private readonly int _primaryAssignmentTypeId;
    private readonly int _activeAssignmentStatusId;
    private readonly int _activeCompStatusId;
    private readonly int _approvedApprovalStatusId;

    public EmploymentService(
        IConnectionFactory         connectionFactory,
        IPersonRepository          personRepository,
        IPersonAddressRepository   personAddressRepository,
        IEmploymentRepository      employmentRepository,
        IAssignmentRepository      assignmentRepository,
        ICompensationRepository    compensationRepository,
        IEmployeeEventRepository   eventRepository,
        IEventPublisher            eventPublisher,
        ITemporalContext           temporalContext,
        ILookupCache               lookupCache)
    {
        _connectionFactory       = connectionFactory;
        _personRepository        = personRepository;
        _personAddressRepository = personAddressRepository;
        _employmentRepository    = employmentRepository;
        _assignmentRepository    = assignmentRepository;
        _compensationRepository  = compensationRepository;
        _eventRepository         = eventRepository;
        _eventPublisher          = eventPublisher;
        _temporalContext         = temporalContext;
        _lookupCache             = lookupCache;

        _activeStatusId           = lookupCache.GetId(LookupTables.EmploymentStatus,    "ACTIVE");
        _terminatedStatusId       = lookupCache.GetId(LookupTables.EmploymentStatus,    "TERMINATED");
        _closedStatusId           = lookupCache.GetId(LookupTables.EmploymentStatus,    "CLOSED");
        _onLeaveStatusId          = lookupCache.GetId(LookupTables.EmploymentStatus,    "ON_LEAVE");
        _regularTempStatusId      = lookupCache.GetId(LookupTables.RegularTemporaryStatus, "REGULAR");
        _primaryAssignmentTypeId  = lookupCache.GetId(LookupTables.AssignmentType,      "PRIMARY");
        _activeAssignmentStatusId = lookupCache.GetId(LookupTables.AssignmentStatus,    "ACTIVE");
        _activeCompStatusId       = lookupCache.GetId(LookupTables.CompensationStatus,  "ACTIVE");
        _approvedApprovalStatusId = lookupCache.GetId(LookupTables.ApprovalStatus,      "APPROVED");
    }

    public async Task<HireResult> HireEmployeeAsync(HireEmployeeCommand command)
    {
        ValidateHireCommand(command);

        if (await _employmentRepository.ExistsWithNumberAsync(command.EmployeeNumber))
            throw new DomainException($"Employee number {command.EmployeeNumber} already exists.");

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            var person       = Person.CreateNew(command, _lookupCache);
            var personId     = await _personRepository.InsertAsync(person, uow);

            var address      = PersonAddress.CreateFromHire(command, personId);
            await _personAddressRepository.InsertAsync(address, uow);

            var employment   = Employment.CreateFromHire(command, personId, _lookupCache);
            var employmentId = await _employmentRepository.InsertAsync(employment, uow);

            var assignment   = Assignment.CreateInitial(command, employmentId, _lookupCache);
            await _assignmentRepository.InsertAsync(assignment, uow);

            var compensation = CompensationRecord.CreateInitial(command, employmentId, _lookupCache);
            await _compensationRepository.InsertAsync(compensation, uow);

            var hireEvent = EmployeeEvent.CreateHire(employmentId, command, _lookupCache);
            var eventId   = await _eventRepository.InsertAsync(hireEvent, uow);

            uow.Commit();

            await _eventPublisher.PublishAsync(new HireEventPayload
            {
                EmploymentId     = employmentId,
                PersonId         = personId,
                EventId          = eventId,
                TenantId         = Guid.Empty,
                EffectiveDate    = command.EmploymentStartDate,
                LegalEntityId    = command.LegalEntityId,
                FlsaStatus       = _lookupCache.GetCode(LookupTables.FlsaStatus, command.FlsaStatusId),
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
            e.EmploymentStatusId == _activeStatusId ||
            e.EmploymentStatusId == _onLeaveStatusId);

        if (activeEmployment is not null)
            throw new DomainException("Cannot rehire — person has an active employment record.");

        var priorEmployment = priorEmployments
            .OrderByDescending(e => e.EmploymentStartDate)
            .FirstOrDefault();

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            var now = DateTimeOffset.UtcNow;

            var employment = new Employment
            {
                EmploymentId             = Guid.NewGuid(),
                PersonId                 = personId,
                LegalEntityId            = command.LegalEntityId,
                EmployerId               = command.LegalEntityId,
                EmployeeNumber           = command.EmployeeNumber,
                EmploymentTypeId         = command.EmploymentTypeId,
                EmploymentStartDate      = command.EmploymentStartDate,
                OriginalHireDate         = priorEmployment?.OriginalHireDate ?? command.EmploymentStartDate,
                EmploymentStatusId       = _activeStatusId,
                FullPartTimeStatusId     = command.FullPartTimeStatusId,
                RegularTemporaryStatusId = _regularTempStatusId,
                FlsaStatusId             = command.FlsaStatusId,
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
                CreationTimestamp        = now,
                LastUpdateTimestamp      = now,
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
                PayrollContextId    = command.PayrollContextId,
                AssignmentTypeId    = _primaryAssignmentTypeId,
                AssignmentStatusId  = _activeAssignmentStatusId,
                AssignmentStartDate = command.EmploymentStartDate,
                CreatedBy           = command.InitiatedBy,
                CreationTimestamp   = now,
                LastUpdatedBy       = command.InitiatedBy,
                LastUpdateTimestamp = now
            };

            await _assignmentRepository.InsertAsync(assignment, uow);

            var rateTypeCode = _lookupCache.GetCode(LookupTables.CompensationRateType, command.RateTypeId);
            var freqCode     = _lookupCache.GetCode(LookupTables.PayFrequency,          command.PayFrequencyId);
            var compensation = new CompensationRecord
            {
                CompensationId       = Guid.NewGuid(),
                EmploymentId         = employmentId,
                RateTypeId           = command.RateTypeId,
                BaseRate             = command.BaseRate,
                RateCurrency         = "USD",
                AnnualEquivalent     = CompensationRecord.ComputeAnnualEquivalent(rateTypeCode, command.BaseRate, freqCode),
                PayFrequencyId       = command.PayFrequencyId,
                EffectiveStartDate   = command.EmploymentStartDate,
                CompensationStatusId = _activeCompStatusId,
                ChangeReasonCode     = command.ChangeReasonCode,
                ApprovalStatusId     = _approvedApprovalStatusId,
                PrimaryRateFlag      = true,
                CreatedBy            = command.InitiatedBy,
                CreationTimestamp    = now,
                LastUpdatedBy        = command.InitiatedBy,
                LastUpdateTimestamp  = now
            };

            await _compensationRepository.InsertAsync(compensation, uow);

            var rehireEvent = EmployeeEvent.CreateRehire(employmentId, command, _lookupCache);
            var eventId     = await _eventRepository.InsertAsync(rehireEvent, uow);

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

        if (employment.EmploymentStatusId == _terminatedStatusId ||
            employment.EmploymentStatusId == _closedStatusId)
            throw new DomainException("Employment is already terminated.");

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _employmentRepository.UpdateStatusAsync(
                command.EmploymentId, _terminatedStatusId, command.TerminationDate, uow);

            var terminationEvent = EmployeeEvent.CreateTermination(command.EmploymentId, command, _lookupCache);
            var eventId = await _eventRepository.InsertAsync(terminationEvent, uow);

            uow.Commit();

            await _eventPublisher.PublishAsync(new TerminationEventPayload
            {
                EmploymentId    = command.EmploymentId,
                PersonId        = employment.PersonId,
                EventId         = eventId,
                TenantId        = Guid.Empty,
                TerminationDate = command.TerminationDate,
                EventType       = "TERMINATION",
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
        if (employment.EmploymentStatusId == _terminatedStatusId ||
            employment.EmploymentStatusId == _closedStatusId) return null;

        return employment;
    }

    public async Task<PagedResult<EmploymentListItem>> GetPagedListAsync(EmployeeListQuery query)
        => await _employmentRepository.GetPagedListAsync(query);

    public async Task<EmployeeStatCards> GetStatCardsAsync(DateOnly asOf)
        => await _employmentRepository.GetStatCardsAsync(asOf);

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
    private readonly ILookupCache             _lookupCache;

    private readonly int _activeCompStatusId;
    private readonly int _approvedApprovalStatusId;

    public CompensationService(
        IConnectionFactory       connectionFactory,
        ICompensationRepository  compensationRepository,
        IEmployeeEventRepository eventRepository,
        IEventPublisher          eventPublisher,
        ITemporalContext         temporalContext,
        ILookupCache             lookupCache)
    {
        _connectionFactory      = connectionFactory;
        _compensationRepository = compensationRepository;
        _eventRepository        = eventRepository;
        _eventPublisher         = eventPublisher;
        _temporalContext        = temporalContext;
        _lookupCache            = lookupCache;

        _activeCompStatusId       = lookupCache.GetId(LookupTables.CompensationStatus, "ACTIVE");
        _approvedApprovalStatusId = lookupCache.GetId(LookupTables.ApprovalStatus,     "APPROVED");
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

            var now          = DateTimeOffset.UtcNow;
            var rateTypeCode = _lookupCache.GetCode(LookupTables.CompensationRateType, command.RateTypeId);
            var freqCode     = _lookupCache.GetCode(LookupTables.PayFrequency,          command.PayFrequencyId);
            var newRecord = new CompensationRecord
            {
                CompensationId       = Guid.NewGuid(),
                EmploymentId         = command.EmploymentId,
                RateTypeId           = command.RateTypeId,
                BaseRate             = command.NewBaseRate,
                RateCurrency         = "USD",
                AnnualEquivalent     = CompensationRecord.ComputeAnnualEquivalent(rateTypeCode, command.NewBaseRate, freqCode),
                PayFrequencyId       = command.PayFrequencyId,
                EffectiveStartDate   = command.EffectiveDate,
                CompensationStatusId = _activeCompStatusId,
                ChangeReasonCode     = command.ChangeReasonCode,
                ApprovalStatusId     = _approvedApprovalStatusId,
                PrimaryRateFlag      = true,
                CreatedBy            = command.InitiatedBy,
                CreationTimestamp    = now,
                LastUpdatedBy        = command.InitiatedBy,
                LastUpdateTimestamp  = now
            };

            await _compensationRepository.InsertAsync(newRecord, uow);

            var compEvent = EmployeeEvent.CreateCompensationChange(command.EmploymentId, command, _lookupCache);
            var eventId   = await _eventRepository.InsertAsync(compEvent, uow);

            uow.Commit();

            await _eventPublisher.PublishAsync(new CompensationChangeEventPayload
            {
                EmploymentId   = command.EmploymentId,
                PersonId       = Guid.Empty,
                EventId        = eventId,
                TenantId       = Guid.Empty,
                EffectiveDate  = command.EffectiveDate,
                RateType       = _lookupCache.GetCode(LookupTables.CompensationRateType, command.RateTypeId),
                NewBaseRate    = command.NewBaseRate,
                PayFrequency   = _lookupCache.GetCode(LookupTables.PayFrequency, command.PayFrequencyId),
                IsRetroactive  = isRetroactive,
                EventTimestamp = now
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
    Task<EmployeeEvent>  InitiateEventAsync(Guid employmentId, int eventTypeId,
                             string reasonCode, Guid initiatedBy);
    Task                 ApproveEventAsync(Guid eventId, Guid approvedBy);
    Task                 RejectEventAsync(Guid eventId, Guid rejectedBy, string reason);
    Task<EmployeeEvent?> GetPendingEventAsync(Guid employmentId, int eventTypeId);
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

    public async Task<EmployeeEvent> InitiateEventAsync(Guid employmentId, int eventTypeId,
        string reasonCode, Guid initiatedBy)
    {
        var employeeEvent = new EmployeeEvent
        {
            EventId           = Guid.NewGuid(),
            EmploymentId      = employmentId,
            EventTypeId       = eventTypeId,
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

    public async Task<EmployeeEvent?> GetPendingEventAsync(Guid employmentId, int eventTypeId)
    {
        var events = await _eventRepository.GetByEmploymentIdAsync(employmentId);
        return events.FirstOrDefault(e => e.EventTypeId == eventTypeId);
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

    private readonly int _legalEntityTypeId;
    private readonly int _departmentTypeId;
    private readonly int _locationTypeId;

    public OrgStructureService(IOrgUnitRepository orgUnitRepository, ILookupCache lookupCache)
    {
        _orgUnitRepository  = orgUnitRepository;
        _legalEntityTypeId  = lookupCache.GetId(LookupTables.OrgUnitType, "LEGAL_ENTITY");
        _departmentTypeId   = lookupCache.GetId(LookupTables.OrgUnitType, "DEPARTMENT");
        _locationTypeId     = lookupCache.GetId(LookupTables.OrgUnitType, "LOCATION");
    }

    public async Task<OrgUnit?> GetOrgUnitByIdAsync(Guid orgUnitId)
        => await _orgUnitRepository.GetByIdAsync(orgUnitId);

    public async Task<IEnumerable<OrgUnit>> GetLegalEntitiesAsync()
        => await _orgUnitRepository.GetByTypeAsync(_legalEntityTypeId);

    public async Task<IEnumerable<OrgUnit>> GetDepartmentsAsync()
        => await _orgUnitRepository.GetByTypeAsync(_departmentTypeId);

    public async Task<IEnumerable<OrgUnit>> GetLocationsAsync()
        => await _orgUnitRepository.GetByTypeAsync(_locationTypeId);

    public async Task<IEnumerable<OrgUnit>> GetChildrenAsync(Guid parentOrgUnitId)
        => await _orgUnitRepository.GetChildrenAsync(parentOrgUnitId);

    public async Task<IEnumerable<OrgUnit>> GetAllActiveAsync()
        => await _orgUnitRepository.GetAllActiveAsync();
}
