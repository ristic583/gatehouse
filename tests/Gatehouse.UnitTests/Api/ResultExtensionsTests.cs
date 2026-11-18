using System.Text.Json;
using Gatehouse.Api.Http;
using Gatehouse.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.UnitTests.Api;

public sealed class ResultExtensionsTests
{
    [Fact]
    public async Task Validation_maps_to_400_with_code()
    {
        var (status, body) = await ExecuteAsync(Result.Failure(Error.Validation("tenant.invalid_id", "bad id")).ToHttpResult());

        Assert.Equal(400, status);
        Assert.Equal("tenant.invalid_id", body.GetProperty("code").GetString());
        Assert.Equal("bad id", body.GetProperty("title").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task NotFound_maps_to_404()
    {
        Result<int> result = Error.NotFound("area.not_found", "missing");

        var (status, body) = await ExecuteAsync(result.ToHttpResult(Results.Ok));

        Assert.Equal(404, status);
        Assert.Equal("area.not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Forbidden_maps_to_403()
    {
        var (status, body) = await ExecuteAsync(Result.Failure(Error.Forbidden("tenant.unknown", "nope")).ToHttpResult());

        Assert.Equal(403, status);
        Assert.Equal("tenant.unknown", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Conflict_maps_to_409()
    {
        var (status, body) = await ExecuteAsync(Result.Failure(Error.Conflict("tenant.already_exists", "exists")).ToHttpResult());

        Assert.Equal(409, status);
        Assert.Equal("tenant.already_exists", body.GetProperty("code").GetString());
    }

    private static async Task<(int Status, JsonElement Body)> ExecuteAsync(IResult result)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var body = new MemoryStream();
        context.Response.Body = body;

        await result.ExecuteAsync(context);

        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }
}
