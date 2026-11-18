using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Security;

/// <summary>One mapped API route as the host sees it (pattern, method, route parameters, authorization policies).</summary>
public sealed record ApiRoute(string Pattern, string Method, IReadOnlyList<string> GuidParameters, IReadOnlyList<string> OtherParameters, IReadOnlyList<string> Policies)
{
    public bool IsRead => Method == HttpMethods.Get;

    /// <summary>The pattern with every route parameter replaced by <paramref name="value"/>.</summary>
    public string Url(Func<string, string> value) =>
        Regex.Replace(Pattern, @"\{(\w+)(:[^}]*)?\}", m => value(m.Groups[1].Value), RegexOptions.None, TimeSpan.FromSeconds(1));

    public override string ToString() => $"{Method} {Pattern}";
}

/// <summary>
/// Reads the routes from the running host's <see cref="EndpointDataSource"/>, so security tests generated from it also cover routes
/// added later without editing the test.
/// </summary>
public static class ApiRouteCatalog
{
    public static IReadOnlyList<ApiRoute> Routes(IServiceProvider services)
    {
        var routes = new List<ApiRoute>();
        foreach (var endpoint in services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var pattern = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            if (!pattern.StartsWith("/api/v1/", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = endpoint.RoutePattern.Parameters;
            var guids = parameters.Where(p => p.ParameterPolicies.Any(pp => pp.Content == "guid")).Select(p => p.Name).ToList();
            var others = parameters.Select(p => p.Name).Except(guids).ToList();
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).OfType<string>().Distinct().ToList();
            foreach (var method in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
            {
                routes.Add(new ApiRoute(pattern, method, guids, others, policies));
            }
        }

        return routes;
    }
}
