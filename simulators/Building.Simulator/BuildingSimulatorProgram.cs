namespace Building.Simulator;

public static class BuildingSimulatorProgram
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var legacyPacsUrl = builder.Configuration["Simulator:LegacyPacsUrl"]
            ?? throw new InvalidOperationException("'Simulator:LegacyPacsUrl' is not configured.");
        builder.Services.AddSingleton(TimeProvider.System);
        var cloudPacsUrl = builder.Configuration["Simulator:CloudPacsUrl"];
        builder.Services.AddHttpClient<LegacyPacsControllerClient>(c => c.BaseAddress = new Uri(legacyPacsUrl));
        if (!string.IsNullOrWhiteSpace(cloudPacsUrl))
        {
            builder.Services.AddHttpClient<CloudPacsControllerClient>(c => c.BaseAddress = new Uri(cloudPacsUrl));
        }

        builder.Services.AddSingleton(sp => new DoorControllerRouter(
            sp.GetRequiredService<LegacyPacsControllerClient>(),
            string.IsNullOrWhiteSpace(cloudPacsUrl) ? null : sp.GetRequiredService<CloudPacsControllerClient>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<DoorControllerRouter>>()));
        builder.Services.AddSingleton<DoorSequencer>();
        builder.Services.AddTransient<BuildingSimulator>();
        builder.Services.AddHostedService<ActivitySimulator>();

        var app = builder.Build();
        MapEndpoints(app);
        await app.RunAsync();
    }

    public static void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet("/", () => Results.Content(DevPanel.Html, "text/html"));
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/doors", async (DoorControllerRouter router, CancellationToken ct) => Results.Ok(await router.ListDoorsAsync(ct)));
        app.MapPost("/swipes", async (SwipeCommand command, BuildingSimulator simulator, CancellationToken ct) =>
            await simulator.SwipeAsync(command, ct) is { } decision ? Results.Ok(decision) : Results.NotFound());
        app.MapPost("/scenarios/{name}/clear", async (string name, BuildingSimulator simulator, CancellationToken ct) =>
            await simulator.ClearScenarioAsync(name, ct) ? Results.NoContent() : Results.NotFound());
        app.MapPost("/scenarios/{name}", async (string name, ScenarioRequest? request, BuildingSimulator simulator, CancellationToken ct) =>
            await simulator.RunScenarioAsync(name, request?.DoorRef, ct) ? Results.Accepted() : Results.NotFound());
        app.MapDelete("/scenarios/{name}", async (string name, BuildingSimulator simulator, CancellationToken ct) =>
            await simulator.ClearScenarioAsync(name, ct) ? Results.NoContent() : Results.NotFound());
    }
}
