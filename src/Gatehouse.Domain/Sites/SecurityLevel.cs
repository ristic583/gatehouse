using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Sites;

public sealed class SecurityLevel : AggregateRoot<SecurityLevelId>
{
    private SecurityLevel(SecurityLevelId id, string name, int rank, ApproverChain approverChain, int maxDurationDays)
        : base(id)
    {
        Name = name;
        Rank = rank;
        ApproverChain = approverChain;
        MaxDurationDays = maxDurationDays;
    }

    public string Name { get; private set; }

    public int Rank { get; private set; }

    public ApproverChain ApproverChain { get; private set; }

    public int MaxDurationDays { get; private set; }

    public static Result<SecurityLevel> Create(string name, int rank, ApproverChain approverChain, int maxDurationDays)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return Error.Validation("security_level.invalid_name", "Security level name must be 1-100 characters.");
        }

        if (rank < 1)
        {
            return Error.Validation("security_level.invalid_rank", "Rank must be at least 1.");
        }

        if (maxDurationDays is < 1 or > 3650)
        {
            return Error.Validation("security_level.invalid_max_duration", "Max duration must be 1-3650 days.");
        }

        return new SecurityLevel(SecurityLevelId.New(), name, rank, approverChain, maxDurationDays);
    }
}
