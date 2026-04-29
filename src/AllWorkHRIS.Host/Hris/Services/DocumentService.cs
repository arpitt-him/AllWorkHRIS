using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Host.Hris.Commands;
using AllWorkHRIS.Host.Hris.Domain;
using AllWorkHRIS.Host.Hris.Repositories;

namespace AllWorkHRIS.Host.Hris.Services;

public interface IDocumentService
{
    Task<Guid>                    UploadDocumentAsync(UploadDocumentCommand command,
                                      CancellationToken ct = default);
    Task                          VerifyDocumentAsync(VerifyDocumentCommand command);
    Task<Guid>                    SupersedeDocumentAsync(SupersedeDocumentCommand command,
                                      CancellationToken ct = default);
    Task                          ArchiveDocumentAsync(Guid documentId, Guid archivedBy);
    Task<IEnumerable<HrDocument>> GetDocumentsAsync(Guid personId,
                                      Guid? employmentId = null);
    Task<Stream>                  DownloadDocumentAsync(Guid documentId, Guid requestedBy);
    Task<IEnumerable<HrDocument>> GetExpiringDocumentsAsync(int withinDays,
                                      string? documentType = null);
    Task                          ExpireDocumentAsync(Guid documentId);
    Task<bool>                    IsI9VerifiedAsync(Guid personId, Guid employmentId);
}

public sealed class DocumentService : IDocumentService
{
    private readonly IConnectionFactory      _connectionFactory;
    private readonly IDocumentRepository     _documentRepository;
    private readonly IDocumentStorageService _storageService;
    private readonly ILookupCache            _lookupCache;

    private readonly int _activeStatusId;
    private readonly int _supersededStatusId;
    private readonly int _expiredStatusId;
    private readonly int _archivedStatusId;
    private readonly int _i9TypeId;

    public DocumentService(
        IConnectionFactory      connectionFactory,
        IDocumentRepository     documentRepository,
        IDocumentStorageService storageService,
        ILookupCache            lookupCache)
    {
        _connectionFactory  = connectionFactory;
        _documentRepository = documentRepository;
        _storageService     = storageService;
        _lookupCache        = lookupCache;

        _activeStatusId     = lookupCache.GetId(LookupTables.DocumentStatus, "ACTIVE");
        _supersededStatusId = lookupCache.GetId(LookupTables.DocumentStatus, "SUPERSEDED");
        _expiredStatusId    = lookupCache.GetId(LookupTables.DocumentStatus, "EXPIRED");
        _archivedStatusId   = lookupCache.GetId(LookupTables.DocumentStatus, "ARCHIVED");
        _i9TypeId           = lookupCache.GetId(LookupTables.DocumentType, "I9");
    }

    public async Task<Guid> UploadDocumentAsync(
        UploadDocumentCommand command, CancellationToken ct = default)
    {
        if (command.ExpirationDate.HasValue
         && command.ExpirationDate.Value <= command.EffectiveDate)
            throw new ValidationException("Expiration date must be after effective date.");

        var documentTypeId = _lookupCache.GetId(LookupTables.DocumentType, command.DocumentType);

        var documentId = Guid.NewGuid();
        var storageRef = await _storageService.StoreAsync(
            documentId, command.FileContent, command.FileFormat, ct);

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            var existing = await _documentRepository.GetActiveByTypeAsync(
                command.PersonId, documentTypeId, command.EmploymentId);

            if (existing is not null)
            {
                await _documentRepository.UpdateStatusAsync(
                    existing.DocumentId, _supersededStatusId, uow);
                await _documentRepository.SetSupersededByAsync(
                    existing.DocumentId, documentId, uow);
            }

            var document = new HrDocument
            {
                DocumentId        = documentId,
                PersonId          = command.PersonId,
                EmploymentId      = command.EmploymentId,
                DocumentTypeId    = documentTypeId,
                DocumentName      = command.DocumentName,
                DocumentVersion   = (existing?.DocumentVersion ?? 0) + 1,
                DocumentStatusId  = _activeStatusId,
                EffectiveDate     = command.EffectiveDate,
                ExpirationDate    = command.ExpirationDate,
                StorageReference  = storageRef,
                FileFormat        = command.FileFormat,
                UploadDate        = DateTimeOffset.UtcNow,
                UploadedBy        = command.UploadedBy,
                LegalHoldFlag     = false,
                CreatedBy         = command.UploadedBy,
                CreationTimestamp = DateTimeOffset.UtcNow
            };

            await _documentRepository.InsertAsync(document, uow);
            uow.Commit();

            return documentId;
        }
        catch
        {
            uow.Rollback();
            await _storageService.DeleteAsync(storageRef, ct);
            throw;
        }
    }

    public async Task VerifyDocumentAsync(VerifyDocumentCommand command)
    {
        var doc = await _documentRepository.GetByIdAsync(command.DocumentId)
            ?? throw new NotFoundException(nameof(HrDocument), command.DocumentId);

        if (doc.DocumentStatusId != _activeStatusId)
            throw new DomainException("Only active documents can be verified.");

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _documentRepository.SetVerifiedAsync(
                command.DocumentId, command.VerifiedBy, DateTimeOffset.UtcNow, uow);
            uow.Commit();
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public async Task<Guid> SupersedeDocumentAsync(
        SupersedeDocumentCommand command, CancellationToken ct = default)
    {
        var prior = await _documentRepository.GetByIdAsync(command.SupersededDocumentId)
            ?? throw new NotFoundException(nameof(HrDocument), command.SupersededDocumentId);

        if (command.ExpirationDate.HasValue
         && command.ExpirationDate.Value <= command.EffectiveDate)
            throw new ValidationException("Expiration date must be after effective date.");

        var documentTypeId = _lookupCache.GetId(LookupTables.DocumentType, command.DocumentType);

        var documentId = Guid.NewGuid();
        var storageRef = await _storageService.StoreAsync(
            documentId, command.FileContent, command.FileFormat, ct);

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _documentRepository.UpdateStatusAsync(prior.DocumentId, _supersededStatusId, uow);
            await _documentRepository.SetSupersededByAsync(prior.DocumentId, documentId, uow);

            var document = new HrDocument
            {
                DocumentId        = documentId,
                PersonId          = command.PersonId,
                EmploymentId      = command.EmploymentId,
                DocumentTypeId    = documentTypeId,
                DocumentName      = command.DocumentName,
                DocumentVersion   = prior.DocumentVersion + 1,
                DocumentStatusId  = _activeStatusId,
                EffectiveDate     = command.EffectiveDate,
                ExpirationDate    = command.ExpirationDate,
                StorageReference  = storageRef,
                FileFormat        = command.FileFormat,
                UploadDate        = DateTimeOffset.UtcNow,
                UploadedBy        = command.UploadedBy,
                LegalHoldFlag     = false,
                CreatedBy         = command.UploadedBy,
                CreationTimestamp = DateTimeOffset.UtcNow
            };

            await _documentRepository.InsertAsync(document, uow);
            uow.Commit();

            return documentId;
        }
        catch
        {
            uow.Rollback();
            await _storageService.DeleteAsync(storageRef, ct);
            throw;
        }
    }

    public async Task ArchiveDocumentAsync(Guid documentId, Guid archivedBy)
    {
        var doc = await _documentRepository.GetByIdAsync(documentId)
            ?? throw new NotFoundException(nameof(HrDocument), documentId);

        if (doc.LegalHoldFlag)
            throw new DomainException("Cannot archive a document under legal hold.");

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _documentRepository.UpdateStatusAsync(documentId, _archivedStatusId, uow);
            uow.Commit();
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public async Task<IEnumerable<HrDocument>> GetDocumentsAsync(
        Guid personId, Guid? employmentId = null)
    {
        if (employmentId.HasValue)
            return await _documentRepository.GetByEmploymentIdAsync(employmentId.Value);

        return await _documentRepository.GetByPersonIdAsync(personId);
    }

    public async Task<Stream> DownloadDocumentAsync(Guid documentId, Guid requestedBy)
    {
        var doc = await _documentRepository.GetByIdAsync(documentId)
            ?? throw new NotFoundException(nameof(HrDocument), documentId);

        var stream = await _storageService.RetrieveAsync(doc.StorageReference);

        await _documentRepository.LogDownloadAsync(
            documentId, requestedBy, DateTimeOffset.UtcNow);

        return stream;
    }

    public async Task<IEnumerable<HrDocument>> GetExpiringDocumentsAsync(
        int withinDays, string? documentType = null)
    {
        int? documentTypeId = documentType is not null
            ? _lookupCache.GetId(LookupTables.DocumentType, documentType)
            : null;

        return await _documentRepository.GetExpiringWithinAsync(withinDays, documentTypeId);
    }

    public async Task ExpireDocumentAsync(Guid documentId)
    {
        var doc = await _documentRepository.GetByIdAsync(documentId)
            ?? throw new NotFoundException(nameof(HrDocument), documentId);

        if (doc.DocumentStatusId == _expiredStatusId)
            return;

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _documentRepository.UpdateStatusAsync(documentId, _expiredStatusId, uow);
            uow.Commit();
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public async Task<bool> IsI9VerifiedAsync(Guid personId, Guid employmentId)
    {
        var i9 = await _documentRepository.GetActiveByTypeAsync(
            personId, _i9TypeId, employmentId);

        return i9 is not null
            && i9.VerifiedBy.HasValue
            && i9.DocumentStatusId == _activeStatusId;
    }
}
