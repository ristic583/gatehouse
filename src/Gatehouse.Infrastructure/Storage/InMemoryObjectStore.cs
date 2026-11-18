using System.Collections.Concurrent;
using Gatehouse.Application.Credentials.Photos;

namespace Gatehouse.Infrastructure.Storage;

/// <summary>Test/dev object store (<c>Storage:Provider = memory</c>).</summary>
public sealed class InMemoryObjectStore : IObjectStore
{
    private readonly ConcurrentDictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => _objects.Keys.ToList();

    public Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct)
    {
        _objects[key] = content.ToArray();
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken ct) =>
        Task.FromResult(_objects.TryGetValue(key, out var value) ? value.ToArray() : null);

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        _objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
