namespace Gatehouse.Application.Identity.IdentityProviders;

public sealed class IdentityProviderOptions
{
    public const string SectionName = "Identity";

    /// <summary><c>host:port</c> (or bare host) entries that may use HTTP and private addresses (Development only).</summary>
    public IList<string> AllowedInsecureHosts { get; } = [];
}
