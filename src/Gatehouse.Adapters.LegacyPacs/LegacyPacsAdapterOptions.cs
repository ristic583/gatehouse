namespace Gatehouse.Adapters.LegacyPacs;

public sealed record LegacyPacsAdapterOptions(string ConnectionString, TimeSpan ProcessingTimeout, TimeSpan PollInterval)
{
    public LegacyPacsAdapterOptions(string connectionString)
        : this(connectionString, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(250))
    {
    }
}
