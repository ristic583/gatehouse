using System.Diagnostics;
using Gatehouse.Domain.Common;

namespace Gatehouse.Api.Http;

public static class ResultExtensions
{
    public const string ValidationFailedCode = "validation.failed";

    public static IResult ToHttpResult(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
    }

    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);
        return result.IsSuccess ? onSuccess(result.Value) : result.Error.ToProblem();
    }

    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ProblemHttpResult(StatusCodeFor(error.Kind), error.Message, error.Code, errors: null);
    }

    public static IResult ToValidationProblem(
        this IDictionary<string, string[]> errors,
        string code = ValidationFailedCode,
        string title = "One or more validation errors occurred.")
    {
        ArgumentNullException.ThrowIfNull(errors);
        return new ProblemHttpResult(StatusCodes.Status400BadRequest, title, code, new Dictionary<string, string[]>(errors));
    }

    public static int StatusCodeFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status500InternalServerError,
    };

    private sealed class ProblemHttpResult(int statusCode, string title, string code, Dictionary<string, string[]>? errors)
        : IResult, IStatusCodeHttpResult
    {
        public int? StatusCode => statusCode;

        public Task ExecuteAsync(HttpContext httpContext)
        {
            var extensions = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier,
            };

            if (errors is not null)
            {
                extensions["errors"] = errors;
            }

            return Results.Problem(title: title, statusCode: statusCode, extensions: extensions).ExecuteAsync(httpContext);
        }
    }
}
