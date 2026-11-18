namespace Gatehouse.Domain.Sites;

public readonly record struct ReaderId(Guid Value)
{
    public static ReaderId New() => new(Guid.CreateVersion7());
}
