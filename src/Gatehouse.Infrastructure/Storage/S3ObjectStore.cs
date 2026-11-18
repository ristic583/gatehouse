using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Gatehouse.Application.Credentials.Photos;
using Microsoft.Extensions.Configuration;

namespace Gatehouse.Infrastructure.Storage;

/// <summary>
/// S3-compatible object store (MinIO in the AppHost): <c>Storage:Endpoint</c>, <c>Storage:AccessKey</c>, <c>Storage:SecretKey</c>,
/// <c>Storage:Bucket</c> (default <c>gatehouse-photos</c>), path-style addressing. The bucket is created on first use.
/// </summary>
public sealed class S3ObjectStore : IObjectStore, IDisposable
{
    private readonly Lazy<AmazonS3Client> _client;
    private readonly string _bucket;
    private readonly SemaphoreSlim _bucketLock = new(1, 1);
    private bool _bucketReady;

    public S3ObjectStore(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _bucket = configuration["Storage:Bucket"] ?? "gatehouse-photos";
        _client = new Lazy<AmazonS3Client>(() =>
        {
            var endpoint = configuration["Storage:Endpoint"];
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new InvalidOperationException("Storage:Endpoint is not configured (or set Storage:Provider=memory for tests).");
            }

            return new AmazonS3Client(
                new BasicAWSCredentials(configuration["Storage:AccessKey"], configuration["Storage:SecretKey"]),
                new AmazonS3Config { ServiceURL = endpoint, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        });
    }

    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        using var stream = new MemoryStream(content, writable: false);
        await _client.Value.PutObjectAsync(
            new PutObjectRequest { BucketName = _bucket, Key = key, InputStream = stream, ContentType = contentType }, ct);
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct)
    {
        try
        {
            using var response = await _client.Value.GetObjectAsync(_bucket, key, ct);
            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct) =>
        await _client.Value.DeleteObjectAsync(_bucket, key, ct);

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }

        _bucketLock.Dispose();
    }

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (_bucketReady)
        {
            return;
        }

        await _bucketLock.WaitAsync(ct);
        try
        {
            if (!_bucketReady)
            {
                var buckets = await _client.Value.ListBucketsAsync(ct);
                if (buckets.Buckets?.Any(b => b.BucketName == _bucket) != true)
                {
                    await _client.Value.PutBucketAsync(_bucket, ct);
                }

                _bucketReady = true;
            }
        }
        finally
        {
            _bucketLock.Release();
        }
    }
}
