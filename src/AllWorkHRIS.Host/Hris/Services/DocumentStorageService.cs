using AllWorkHRIS.Host.Hris.Domain;

namespace AllWorkHRIS.Host.Hris.Services;

public interface IDocumentStorageService
{
    Task<string> StoreAsync(Guid documentId, Stream content,
        string fileFormat, CancellationToken ct = default);

    Task<Stream> RetrieveAsync(string storageReference,
        CancellationToken ct = default);

    Task DeleteAsync(string storageReference,
        CancellationToken ct = default);
}

public sealed class LocalFileSystemDocumentStorageService : IDocumentStorageService
{
    private readonly DocumentStorageOptions _options;

    public LocalFileSystemDocumentStorageService(DocumentStorageOptions options)
        => _options = options;

    public async Task<string> StoreAsync(
        Guid documentId, Stream content, string fileFormat, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_options.BasePath);

        var fileName = $"{documentId}.{fileFormat.ToLowerInvariant()}";
        var fullPath = Path.Combine(_options.BasePath, fileName);

        await using var fs = new FileStream(
            fullPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 65536, useAsync: true);

        await content.CopyToAsync(fs, ct);
        return fileName;
    }

    public Task<Stream> RetrieveAsync(string storageReference, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_options.BasePath, storageReference);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Document file not found.", storageReference);

        Stream stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 65536, useAsync: true);

        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageReference, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_options.BasePath, storageReference);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }
}
