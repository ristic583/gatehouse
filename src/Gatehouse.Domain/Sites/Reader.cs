using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Sites;

public sealed class Reader : Entity<ReaderId>
{
    private Reader(ReaderId id, DoorId doorId, string name, ReaderDirection direction, Point position, string? pacsReaderRef)
        : base(id)
    {
        DoorId = doorId;
        Name = name;
        Direction = direction;
        Position = position;
        PacsReaderRef = pacsReaderRef;
    }

    // EF Core
    private Reader()
        : base(default)
    {
        Name = string.Empty;
    }

    public DoorId DoorId { get; private set; }

    public string Name { get; private set; }

    public ReaderDirection Direction { get; private set; }

    public Point Position { get; private set; }

    public string? PacsReaderRef { get; private set; }

    public Result Update(string name, ReaderDirection direction, Point position, string? pacsReaderRef)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return Error.Validation("reader.invalid_name", "Reader name must be 1-100 characters.");
        }

        Name = name;
        Direction = direction;
        Position = position;
        PacsReaderRef = pacsReaderRef;
        return Result.Success();
    }

    public static Result<Reader> Create(DoorId doorId, string name, ReaderDirection direction, Point position, string? pacsReaderRef)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return Error.Validation("reader.invalid_name", "Reader name must be 1-100 characters.");
        }

        return new Reader(ReaderId.New(), doorId, name, direction, position, pacsReaderRef);
    }
}
