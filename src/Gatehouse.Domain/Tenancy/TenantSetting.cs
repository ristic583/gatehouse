namespace Gatehouse.Domain.Tenancy;

/// <summary>Per-tenant key/value setting stored in the tenant database (lifecycle settings and similar).</summary>
public sealed class TenantSetting
{
    public const int KeyMaxLength = 100;

    // EF Core
    private TenantSetting()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    public TenantSetting(string key, string value, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(key.Length, KeyMaxLength, nameof(key));
        ArgumentNullException.ThrowIfNull(value);

        Key = key;
        Value = value;
        UpdatedAt = now;
    }

    public string Key { get; private set; }

    public string Value { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string value, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
        UpdatedAt = now;
    }
}
