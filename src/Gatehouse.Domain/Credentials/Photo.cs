using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Credentials;

/// <summary>A person's photo (domain-model §2.3): stored in object storage under <see cref="ObjectKey"/>, JPEG or PNG, at most 2
/// MiB.</summary>
public sealed class Photo
{
    public const long MaxSizeBytes = 2 * 1024 * 1024;
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";

    private Photo(PersonId personId, string objectKey, string contentType, long sizeBytes, DateTimeOffset uploadedAt)
    {
        PersonId = personId;
        ObjectKey = objectKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        UploadedAt = uploadedAt;
    }

    public PersonId PersonId { get; private set; }

    public string ObjectKey { get; private set; }

    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    public static Result<Photo> Create(PersonId personId, string objectKey, string contentType, long sizeBytes, DateTimeOffset now)
    {
        var valid = Validate(objectKey, contentType, sizeBytes);
        return valid.IsFailure ? valid.Error : new Photo(personId, objectKey, contentType, sizeBytes, now);
    }

    /// <summary>Points the photo at a new object; the caller deletes the old object.</summary>
    public Result Replace(string objectKey, string contentType, long sizeBytes, DateTimeOffset now)
    {
        var valid = Validate(objectKey, contentType, sizeBytes);
        if (valid.IsFailure)
        {
            return valid;
        }

        (ObjectKey, ContentType, SizeBytes, UploadedAt) = (objectKey, contentType, sizeBytes, now);
        return Result.Success();
    }

    private static Result Validate(string objectKey, string contentType, long sizeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        if (contentType is not (Jpeg or Png))
        {
            return Error.Validation("photo.unsupported_type", "Only JPEG and PNG photos are accepted.");
        }

        return sizeBytes is <= 0 or > MaxSizeBytes
            ? Error.Validation("photo.too_large", "A photo may be at most 2 MiB.")
            : Result.Success();
    }
}
