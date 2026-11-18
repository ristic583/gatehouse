using Gatehouse.Domain.Platform;

namespace Gatehouse.Application.Platform;

/// <summary>The signed-in platform operator (scoped; resolved by <see cref="PlatformOperatorHandler"/>).</summary>
public interface IPlatformOperator
{
    public bool IsResolved { get; }

    public PlatformOperatorId Id { get; }

    public string Subject { get; }

    public string Email { get; }

    public string DisplayName { get; }
}
