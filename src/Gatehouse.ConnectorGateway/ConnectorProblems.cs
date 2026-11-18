using System.Diagnostics;
using Gatehouse.Domain.Common;
using Microsoft.AspNetCore.Http;

namespace Gatehouse.ConnectorGateway;

/// <summary>problem+json per connector-protocol.md §5.</summary>
public static class ConnectorProblems
{
    public static IResult Create(
        HttpContext http, string code, int status, string title, string detail, IDictionary<string, object?>? extra = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["traceId"] = Activity.Current?.Id ?? http.TraceIdentifier,
        };
        if (extra is not null)
        {
            foreach (var (k, v) in extra)
            {
                extensions[k] = v;
            }
        }

        return Results.Problem(
            detail: detail,
            statusCode: status,
            title: title,
            type: $"https://gatehouse.dev/problems/{code}",
            extensions: extensions);
    }

    public static IResult FromError(HttpContext http, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var status = error.Code == "connector.payload_too_large" ? 413 : error.Kind switch
        {
            ErrorKind.Validation => 400,
            ErrorKind.Unauthorized => 401,
            ErrorKind.Forbidden => 403,
            ErrorKind.NotFound => 404,
            ErrorKind.Conflict => 409,
            _ => 500,
        };
        return Create(http, error.Code, status, error.Message.TrimEnd('.'), error.Message);
    }
}
