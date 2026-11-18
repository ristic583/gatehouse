using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace Gatehouse.IntegrationTests.Infrastructure;

/// <summary>
/// Testcontainers reuse for this project's containers: each has a fixed name <c>gatehouse-test-{role}</c>, the label
/// <c>gatehouse-test={role}</c> and a configuration key. A container with the same role but another key (an older
/// configuration) is removed first, so the fixed name is free. Only containers carrying these labels are ever touched.
/// </summary>
public static class ReusedContainers
{
    public const string RoleLabel = "gatehouse-test";
    public const string KeyLabel = "gatehouse-test.key";

    /// <summary>Names, labels and reuse-hashes the container; reuse keeps it running after the run.</summary>
    public static TBuilder Reuse<TBuilder, TContainer, TConfiguration>(this ContainerBuilder<TBuilder, TContainer, TConfiguration> builder, string role, string key)
        where TBuilder : ContainerBuilder<TBuilder, TContainer, TConfiguration>
        where TContainer : IContainer
        where TConfiguration : IContainerConfiguration
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .WithName($"{RoleLabel}-{role}")
            .WithLabel(RoleLabel, role)
            .WithLabel(KeyLabel, key)
            .WithReuse(_ => $"{role}:{key}");
    }

    /// <summary>Removes containers of <paramref name="role"/> whose key differs from <paramref name="key"/>.</summary>
    public static async Task RemoveStaleAsync(string role, string key, CancellationToken ct = default)
    {
        using var client = TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientBuilder(ResourceReaper.DefaultSessionId).Build();
        var containers = await client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>(StringComparer.Ordinal)
                {
                    ["label"] = new Dictionary<string, bool>(StringComparer.Ordinal) { [$"{RoleLabel}={role}"] = true },
                },
            },
            ct);
        foreach (var container in containers.Where(c => !c.Labels.TryGetValue(KeyLabel, out var current) || current != key))
        {
            await client.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true }, ct);
        }
    }
}
