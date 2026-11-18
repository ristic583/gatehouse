using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace CloudPacs.Simulator;

/// <summary>In-memory vendor data. Error results are vendor error codes (<c>unknown_cardholder</c>, ...).</summary>
public sealed class CloudPacsStore(IOptions<CloudPacsOptions> options, TimeProvider time)
{
    public const int MaxPageSize = 100;

    private readonly Lock _gate = new();
    private readonly List<SimCardholder> _cardholders = [];
    private readonly List<SimCard> _cards = [];
    private readonly List<SimAssignment> _assignments = [];
    private readonly Dictionary<string, string> _pinHashes = new(StringComparer.Ordinal);

    public IReadOnlyList<SimAccessGroup> Groups => CloudPacsSeed.Groups;

    public IReadOnlyList<SimDoor> Doors => CloudPacsSeed.Doors;

    public SimCardholder UpsertCardholder(string externalRef, CardholderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = time.GetUtcNow();
        lock (_gate)
        {
            var index = _cardholders.FindIndex(c => c.ExternalRef == externalRef);
            SimCardholder holder;
            if (index < 0)
            {
                holder = new SimCardholder(Guid.CreateVersion7(), externalRef, request.GivenName, request.FamilyName, request.PhotoUrl, now);
                _cardholders.Add(holder);
            }
            else
            {
                holder = _cardholders[index] with { GivenName = request.GivenName, FamilyName = request.FamilyName, PhotoUrl = request.PhotoUrl ?? _cardholders[index].PhotoUrl };
                _cardholders[index] = holder;
            }

            if (!string.IsNullOrWhiteSpace(request.CardNumber))
            {
                var card = _cards.FindIndex(c => c.CardNumber == request.CardNumber);
                if (card < 0)
                {
                    _cards.Add(new SimCard(request.CardNumber, externalRef, "active", now));
                }
                else if (_cards[card].ExternalRef != externalRef)
                {
                    _cards[card] = _cards[card] with { ExternalRef = externalRef };
                }
            }

            return holder;
        }
    }

    public string? SetCardState(string cardNumber, string externalRef, string state)
    {
        lock (_gate)
        {
            if (!_cardholders.Exists(c => c.ExternalRef == externalRef))
            {
                return "unknown_cardholder";
            }

            var index = _cards.FindIndex(c => c.CardNumber == cardNumber);
            if (index < 0)
            {
                _cards.Add(new SimCard(cardNumber, externalRef, state, time.GetUtcNow()));
            }
            else
            {
                _cards[index] = _cards[index] with { ExternalRef = externalRef, State = state };
            }

            return null;
        }
    }

    public void SetPin(string externalRef, string pin)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pin)));
        lock (_gate)
        {
            _pinHashes[externalRef] = hash;
        }
    }

    public bool HasPin(string externalRef)
    {
        lock (_gate)
        {
            return _pinHashes.ContainsKey(externalRef);
        }
    }

    /// <summary>Adds a membership (idempotent while active). Returns the assignment or an error code.</summary>
    public (SimAssignment? Assignment, string? Error) AddMember(string groupId, string externalRef, string cardNumber)
    {
        if (!Groups.Any(g => g.Id == groupId))
        {
            return (null, "unknown_access_group");
        }

        lock (_gate)
        {
            if (!_cardholders.Exists(c => c.ExternalRef == externalRef))
            {
                return (null, "unknown_cardholder");
            }

            var existing = _assignments.Find(a => a.Active && a.GroupId == groupId && a.ExternalRef == externalRef);
            if (existing is not null)
            {
                return (existing, null);
            }

            var assignment = new SimAssignment(Guid.CreateVersion7(), groupId, externalRef, cardNumber, true, time.GetUtcNow(), null);
            _assignments.Add(assignment);
            return (assignment, null);
        }
    }

    public string? RemoveMember(string groupId, string externalRef)
    {
        if (!Groups.Any(g => g.Id == groupId))
        {
            return "unknown_access_group";
        }

        lock (_gate)
        {
            var index = _assignments.FindIndex(a => a.Active && a.GroupId == groupId && a.ExternalRef == externalRef);
            if (index >= 0)
            {
                _assignments[index] = _assignments[index] with { Active = false, RemovedAt = time.GetUtcNow() };
            }

            return null;
        }
    }

    public SimDoor? FindDoor(string doorRef) => Doors.FirstOrDefault(d => d.DoorRef == doorRef);

    public CloudPacsPage<SimCardholder> Cardholders(string? cursor, int? limit) => Page(_cardholders, c => c.CreatedAt, cursor, limit);

    public CloudPacsPage<SimCard> Cards(string? cursor, int? limit) => Page(_cards, c => c.CreatedAt, cursor, limit);

    public CloudPacsPage<SimAssignment> Assignments(string? cursor, int? limit) => Page(_assignments, a => a.AddedAt, cursor, limit);

    public IReadOnlyList<SimAssignment> AllAssignments()
    {
        lock (_gate)
        {
            return [.. _assignments];
        }
    }

    public string? HolderName(string externalRef)
    {
        lock (_gate)
        {
            return _cardholders.Find(c => c.ExternalRef == externalRef) is { } c ? $"{c.GivenName} {c.FamilyName}" : null;
        }
    }

    /// <summary>Swipe decision from local data: active card + active membership in the door's group.</summary>
    public (bool Granted, string? CardNumber, string? ExternalRef) Decide(SimDoor door, string? cardNumber, string? externalRef)
    {
        ArgumentNullException.ThrowIfNull(door);
        lock (_gate)
        {
            var card = cardNumber is not null
                ? _cards.Find(c => c.CardNumber == cardNumber)
                : _cards.Find(c => c.ExternalRef == externalRef && c.State == "active");
            if (card is null)
            {
                return (false, cardNumber, externalRef);
            }

            var granted = card.State == "active" && _assignments.Exists(a => a.Active && a.GroupId == door.GroupId && a.ExternalRef == card.ExternalRef);
            return (granted, card.CardNumber, card.ExternalRef);
        }
    }

    public string? CardState(string cardNumber)
    {
        lock (_gate)
        {
            return _cards.Find(c => c.CardNumber == cardNumber)?.State;
        }
    }

    private CloudPacsPage<T> Page<T>(List<T> source, Func<T, DateTimeOffset> createdAt, string? cursor, int? limit)
    {
        var size = Math.Clamp(limit ?? MaxPageSize, 1, MaxPageSize);
        var visibleBefore = time.GetUtcNow().AddMilliseconds(-options.Value.ReadDelayMs);
        var start = int.TryParse(cursor, out var parsed) && parsed > 0 ? parsed : 0;
        lock (_gate)
        {
            var visible = source.Where(x => createdAt(x) <= visibleBefore).ToList();
            var items = visible.Skip(start).Take(size).ToList();
            var next = start + items.Count < visible.Count ? (start + items.Count).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            return new CloudPacsPage<T>(items, next);
        }
    }
}
