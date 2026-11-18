namespace Gatehouse.Application.Credentials.Photos;

/// <summary>Object storage for binary blobs (photos); MinIO/S3 in the AppHost, in-memory in tests.</summary>
public interface IObjectStore
{
    public Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct);

    /// <summary>The object's bytes, or null when it does not exist.</summary>
    public Task<byte[]?> GetAsync(string key, CancellationToken ct);

    public Task DeleteAsync(string key, CancellationToken ct);
}
