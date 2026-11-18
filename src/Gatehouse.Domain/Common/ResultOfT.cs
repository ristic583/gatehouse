namespace Gatehouse.Domain.Common;

#pragma warning disable CA1000 // Factory members on the generic result are intentional.
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error)
        : base(error)
    {
        _value = value;
    }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error.Code}).");

    internal static Result<T> FromValue(T value) => new(value, null);

    internal static Result<T> FromError(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error);
    }

    public static implicit operator Result<T>(T value) => FromValue(value);

    public static implicit operator Result<T>(Error error) => FromError(error);
}
#pragma warning restore CA1000
