namespace Gatehouse.Domain.People;

public readonly record struct PersonId(Guid Value)
{
    public static PersonId New() => new(Guid.CreateVersion7());
}
