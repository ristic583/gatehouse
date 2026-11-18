namespace Gatehouse.UnitTests.Agent;

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Paths { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Paths.Add(request.RequestUri!.AbsolutePath);
        return Task.FromResult(respond(request));
    }
}
