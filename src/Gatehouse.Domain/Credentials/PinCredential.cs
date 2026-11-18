using Gatehouse.Domain.Common;
using Gatehouse.Domain.Credentials.Events;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Tasks;

namespace Gatehouse.Domain.Credentials;

/// <summary>A person's envelope-encrypted PIN (domain-model.md §2.3). Never holds the plain value.</summary>
public sealed class PinCredential : AggregateRoot<PersonId>
{
    private PinCredential(PersonId personId, byte[] ciphertext, string keyId, DateTimeOffset now)
        : base(personId)
    {
        Ciphertext = ciphertext;
        KeyId = keyId;
        SetAt = now;
    }

    // EF Core
    private PinCredential()
        : base(default)
    {
        Ciphertext = [];
        KeyId = string.Empty;
    }

    public PersonId PersonId => Id;

    [Sensitive]
    public byte[] Ciphertext { get; private set; }

    public string KeyId { get; private set; }

    public DateTimeOffset SetAt { get; private set; }

    public static PinCredential Set(PersonId personId, byte[] ciphertext, string keyId, PersonId setBy, DateTimeOffset now)
    {
        Validate(ciphertext, keyId);
        var pin = new PinCredential(personId, ciphertext, keyId, now);
        pin.Raise(new PinSet(personId, setBy, now));
        return pin;
    }

    public void Replace(byte[] ciphertext, string keyId, PersonId setBy, DateTimeOffset now)
    {
        Validate(ciphertext, keyId);
        Ciphertext = ciphertext;
        KeyId = keyId;
        SetAt = now;
        Raise(new PinSet(Id, setBy, now));
    }

    public void RecordReveal(PersonId by, StaffTaskId taskId, DateTimeOffset now) => Raise(new PinRevealed(Id, by, taskId, now));

    private static void Validate(byte[] ciphertext, string keyId)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentOutOfRangeException.ThrowIfZero(ciphertext.Length, nameof(ciphertext));
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
    }
}
