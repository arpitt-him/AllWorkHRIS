using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Host.Hris.Domain;

namespace AllWorkHRIS.Host.Hris.Repositories;

public interface IDocumentRepository
{
    Task<HrDocument?>              GetByIdAsync(Guid documentId);
    Task<IEnumerable<HrDocument>>  GetByPersonIdAsync(Guid personId);
    Task<IEnumerable<HrDocument>>  GetByEmploymentIdAsync(Guid employmentId);
    Task<HrDocument?>              GetActiveByTypeAsync(Guid personId,
                                       int documentTypeId, Guid? employmentId = null);
    Task<IEnumerable<HrDocument>>  GetExpiringWithinAsync(int days,
                                       int? documentTypeId = null);
    Task<IEnumerable<HrDocument>>  GetExpiredAsOfAsync(DateOnly asOf);
    Task<Guid>                     InsertAsync(HrDocument document, IUnitOfWork uow);
    Task                           UpdateStatusAsync(Guid documentId, int statusId, IUnitOfWork uow);
    Task                           SetSupersededByAsync(Guid documentId,
                                       Guid supersedingDocumentId, IUnitOfWork uow);
    Task                           SetVerifiedAsync(Guid documentId, Guid verifiedBy,
                                       DateTimeOffset verificationDate, IUnitOfWork uow);
    Task                           LogDownloadAsync(Guid documentId, Guid accessedBy,
                                       DateTimeOffset accessTimestamp);
}

public sealed class DocumentRepository : IDocumentRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public DocumentRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<HrDocument?> GetByIdAsync(Guid documentId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<HrDocument>(
            "SELECT * FROM document WHERE document_id = @Id",
            new { Id = documentId });
    }

    public async Task<IEnumerable<HrDocument>> GetByPersonIdAsync(Guid personId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<HrDocument>(
            "SELECT * FROM document WHERE person_id = @Id ORDER BY creation_timestamp DESC",
            new { Id = personId });
    }

    public async Task<IEnumerable<HrDocument>> GetByEmploymentIdAsync(Guid employmentId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<HrDocument>(
            "SELECT * FROM document WHERE employment_id = @Id ORDER BY creation_timestamp DESC",
            new { Id = employmentId });
    }

    public async Task<HrDocument?> GetActiveByTypeAsync(Guid personId,
        int documentTypeId, Guid? employmentId = null)
    {
        using var conn = _connectionFactory.CreateConnection();
        const string sql =
            @"SELECT d.* FROM document d
              JOIN lkp_document_status ds ON d.document_status_id = ds.id
              WHERE d.person_id = @PersonId
                AND d.document_type_id = @TypeId
                AND ds.code = 'ACTIVE'
                AND (@EmploymentId IS NULL OR d.employment_id = @EmploymentId)
              ORDER BY d.document_version DESC
              LIMIT 1";
        return await conn.QueryFirstOrDefaultAsync<HrDocument>(sql,
            new { PersonId = personId, TypeId = documentTypeId, EmploymentId = employmentId });
    }

    public async Task<IEnumerable<HrDocument>> GetExpiringWithinAsync(int days,
        int? documentTypeId = null)
    {
        using var conn = _connectionFactory.CreateConnection();
        const string sql =
            @"SELECT d.* FROM document d
              JOIN lkp_document_status ds ON d.document_status_id = ds.id
              WHERE ds.code = 'ACTIVE'
                AND d.expiration_date IS NOT NULL
                AND d.expiration_date <= (CURRENT_DATE + @Days * INTERVAL '1 day')
                AND d.expiration_date > CURRENT_DATE
                AND (@TypeId IS NULL OR d.document_type_id = @TypeId)";
        return await conn.QueryAsync<HrDocument>(sql,
            new { Days = days, TypeId = documentTypeId });
    }

    public async Task<IEnumerable<HrDocument>> GetExpiredAsOfAsync(DateOnly asOf)
    {
        using var conn = _connectionFactory.CreateConnection();
        const string sql =
            @"SELECT d.* FROM document d
              JOIN lkp_document_status ds ON d.document_status_id = ds.id
              WHERE ds.code = 'ACTIVE'
                AND d.expiration_date IS NOT NULL
                AND d.expiration_date <= @AsOf";
        return await conn.QueryAsync<HrDocument>(sql,
            new { AsOf = asOf.ToDateTime(TimeOnly.MinValue) });
    }

    public async Task<Guid> InsertAsync(HrDocument document, IUnitOfWork uow)
    {
        const string sql =
            @"INSERT INTO document (
                document_id, person_id, employment_id, document_type_id,
                document_name, document_version, document_status_id,
                effective_date, expiration_date, storage_reference, file_format,
                upload_date, uploaded_by, legal_hold_flag, created_by, creation_timestamp)
              VALUES (
                @DocumentId, @PersonId, @EmploymentId, @DocumentTypeId,
                @DocumentName, @DocumentVersion, @DocumentStatusId,
                @EffectiveDate, @ExpirationDate, @StorageReference, @FileFormat,
                @UploadDate, @UploadedBy, @LegalHoldFlag, @CreatedBy, @CreationTimestamp)
              RETURNING document_id";

        return await uow.Connection.ExecuteScalarAsync<Guid>(sql,
            new
            {
                document.DocumentId,
                document.PersonId,
                document.EmploymentId,
                document.DocumentTypeId,
                document.DocumentName,
                document.DocumentVersion,
                document.DocumentStatusId,
                EffectiveDate   = document.EffectiveDate.ToDateTime(TimeOnly.MinValue),
                ExpirationDate  = document.ExpirationDate?.ToDateTime(TimeOnly.MinValue),
                document.StorageReference,
                document.FileFormat,
                document.UploadDate,
                document.UploadedBy,
                document.LegalHoldFlag,
                document.CreatedBy,
                document.CreationTimestamp
            },
            uow.Transaction);
    }

    public async Task UpdateStatusAsync(Guid documentId, int statusId, IUnitOfWork uow)
    {
        await uow.Connection.ExecuteAsync(
            "UPDATE document SET document_status_id = @StatusId WHERE document_id = @Id",
            new { Id = documentId, StatusId = statusId },
            uow.Transaction);
    }

    public async Task SetSupersededByAsync(Guid documentId,
        Guid supersedingDocumentId, IUnitOfWork uow)
    {
        await uow.Connection.ExecuteAsync(
            "UPDATE document SET superseded_by_document_id = @NewId WHERE document_id = @Id",
            new { Id = documentId, NewId = supersedingDocumentId },
            uow.Transaction);
    }

    public async Task SetVerifiedAsync(Guid documentId, Guid verifiedBy,
        DateTimeOffset verificationDate, IUnitOfWork uow)
    {
        await uow.Connection.ExecuteAsync(
            @"UPDATE document SET verified_by = @VerifiedBy,
              verification_date = @VerificationDate WHERE document_id = @Id",
            new { Id = documentId, VerifiedBy = verifiedBy, VerificationDate = verificationDate },
            uow.Transaction);
    }

    public async Task LogDownloadAsync(Guid documentId, Guid accessedBy,
        DateTimeOffset accessTimestamp)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO document_download_audit
                (audit_id, document_id, accessed_by, access_timestamp)
              VALUES (@AuditId, @DocumentId, @AccessedBy, @AccessTimestamp)",
            new
            {
                AuditId          = Guid.NewGuid(),
                DocumentId       = documentId,
                AccessedBy       = accessedBy,
                AccessTimestamp  = accessTimestamp
            });
    }
}
