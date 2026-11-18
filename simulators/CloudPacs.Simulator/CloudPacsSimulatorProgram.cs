using Microsoft.Extensions.Options;

namespace CloudPacs.Simulator;

public static class CloudPacsSimulatorProgram
{
    private static readonly string[] CardStates = ["active", "suspended", "revoked"];

    public static void Main(string[] args) => CreateApp(args).Run();

    /// <summary>Builds the simulator app (tests run it in-process with <c>--urls=http://127.0.0.1:0</c>).</summary>
    public static WebApplication CreateApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.Configure<CloudPacsOptions>(builder.Configuration.GetSection("CloudPacs"));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<CloudPacsStore>();
        builder.Services.AddSingleton<CloudPacsChaos>();
        builder.Services.AddSingleton<CloudPacsEvents>();
        builder.Services.AddSingleton<WebhookRegistry>();
        builder.Services.AddHttpClient("webhooks", c => c.Timeout = TimeSpan.FromSeconds(10));
        builder.Services.AddHostedService<WebhookDispatcher>();

        var app = builder.Build();
        app.Use(GuardApiAsync);
        MapApi(app);
        MapDev(app);
        return app;
    }

    private static async Task GuardApiAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.Ordinal))
        {
            await next(context);
            return;
        }

        var options = context.RequestServices.GetRequiredService<IOptions<CloudPacsOptions>>().Value;
        if (!string.Equals(context.Request.Headers["X-Api-Key"], options.ApiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
            return;
        }

        if (context.RequestServices.GetRequiredService<CloudPacsChaos>().NextFailure() is { } status)
        {
            context.Response.StatusCode = status;
            if (status == StatusCodes.Status429TooManyRequests)
            {
                context.Response.Headers.RetryAfter = "1";
            }

            await context.Response.WriteAsJsonAsync(new { error = status == StatusCodes.Status429TooManyRequests ? "rate_limited" : "unavailable" });
            return;
        }

        await next(context);
    }

    private static IResult Error(int status, string code) => Results.Json(new { error = code }, statusCode: status);

    private static void MapApi(WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapPut("/cardholders/{externalRef}", (string externalRef, CardholderRequest request, CloudPacsStore store) =>
        {
            if (string.IsNullOrWhiteSpace(request.GivenName) || string.IsNullOrWhiteSpace(request.FamilyName))
            {
                return Error(StatusCodes.Status400BadRequest, "invalid_cardholder");
            }

            var holder = store.UpsertCardholder(externalRef, request);
            return Results.Ok(new { id = holder.Id, externalRef = holder.ExternalRef });
        });
        api.MapGet("/cardholders", (string? cursor, int? limit, CloudPacsStore store) => Results.Ok(store.Cardholders(cursor, limit)));
        api.MapPut("/cardholders/{externalRef}/pin", (string externalRef, PinRequest request, CloudPacsStore store) =>
        {
            if (request.Pin is not { Length: >= 4 and <= 8 } pin || !pin.All(char.IsAsciiDigit))
            {
                return Error(StatusCodes.Status400BadRequest, "invalid_pin");
            }

            store.SetPin(externalRef, pin);
            return Results.NoContent();
        });
        api.MapPut("/cards/{cardNumber}", (string cardNumber, CardStateRequest request, CloudPacsStore store) =>
        {
            if (!CardStates.Contains(request.State, StringComparer.Ordinal))
            {
                return Error(StatusCodes.Status400BadRequest, "invalid_state");
            }

            return store.SetCardState(cardNumber, request.ExternalRef, request.State) is { } error
                ? Error(StatusCodes.Status404NotFound, error)
                : Results.Ok(new { cardNumber, state = request.State });
        });
        api.MapGet("/cards", (string? cursor, int? limit, CloudPacsStore store) => Results.Ok(store.Cards(cursor, limit)));
        api.MapGet("/access-groups", (CloudPacsStore store) => Results.Ok(store.Groups));
        api.MapPut("/access-groups/{groupId}/members/{externalRef}", (string groupId, string externalRef, MembershipRequest request, CloudPacsStore store) =>
        {
            var (assignment, error) = store.AddMember(groupId, externalRef, request.CardNumber);
            return error is null ? Results.Ok(new { assignmentId = assignment!.AssignmentId }) : Error(StatusCodes.Status404NotFound, error);
        });
        api.MapDelete("/access-groups/{groupId}/members/{externalRef}", (string groupId, string externalRef, CloudPacsStore store) =>
            store.RemoveMember(groupId, externalRef) is { } error ? Error(StatusCodes.Status404NotFound, error) : Results.NoContent());
        api.MapGet("/assignments", (string? cursor, int? limit, CloudPacsStore store) => Results.Ok(store.Assignments(cursor, limit)));
        api.MapPost("/doors/{doorRef}/unlock", (string doorRef, UnlockRequest request, CloudPacsStore store, CloudPacsEvents events,
            TimeProvider time, IHostApplicationLifetime lifetime) =>
        {
            if (request.Seconds is < 1 or > 60)
            {
                return Error(StatusCodes.Status400BadRequest, "invalid_duration");
            }

            if (store.FindDoor(doorRef) is null)
            {
                return Error(StatusCodes.Status404NotFound, "unknown_door");
            }

            events.Emit("door.unlocked", doorRef);
            _ = RelockLaterAsync(events, time, doorRef, TimeSpan.FromSeconds(request.Seconds), lifetime.ApplicationStopping);
            return Results.Accepted();
        });
        api.MapPost("/webhooks", (WebhookRequest request, WebhookRegistry registry) =>
        {
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _) || string.IsNullOrEmpty(request.Secret))
            {
                return Error(StatusCodes.Status400BadRequest, "invalid_webhook");
            }

            return Results.Ok(new { id = registry.Register(request.Url, request.Secret).Id });
        });
        api.MapDelete("/webhooks/{id:guid}", (Guid id, WebhookRegistry registry) =>
            registry.Remove(id) ? Results.NoContent() : Error(StatusCodes.Status404NotFound, "unknown_webhook"));
    }

    private static async Task RelockLaterAsync(CloudPacsEvents events, TimeProvider time, string doorRef, TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, time, ct);
            events.Emit("door.relocked", doorRef);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void MapDev(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapPost("/dev/chaos", (ChaosRequest request, CloudPacsChaos chaos) =>
        {
            chaos.Apply(request);
            return Results.NoContent();
        });
        app.MapDelete("/dev/chaos", (CloudPacsChaos chaos) =>
        {
            chaos.Reset();
            return Results.NoContent();
        });
        app.MapGet("/dev/doors", (CloudPacsStore store) => Results.Ok(store.Doors));
        app.MapPost("/dev/doors/{doorRef}/events", (string doorRef, DoorEventRequest request, CloudPacsStore store, CloudPacsEvents events) =>
        {
            if (!CloudPacsEvents.Types.Contains(request.Type, StringComparer.Ordinal))
            {
                return Error(StatusCodes.Status400BadRequest, "unknown_event_type");
            }

            if (store.FindDoor(doorRef) is null)
            {
                return Error(StatusCodes.Status404NotFound, "unknown_door");
            }

            var e = events.Emit(request.Type, doorRef, request.CardNumber);
            return Results.Ok(new { eventId = e.Sequence, id = e.Id });
        });
        app.MapPost("/dev/swipes", (SwipeRequest request, CloudPacsStore store, CloudPacsEvents events) =>
        {
            if (store.FindDoor(request.DoorRef) is not { } door)
            {
                return Error(StatusCodes.Status404NotFound, "unknown_door");
            }

            var (granted, card, holder) = store.Decide(door, request.CardNumber, request.ExternalRef);
            var reader = "R-" + (door.DoorRef.StartsWith("D-", StringComparison.Ordinal) ? door.DoorRef[2..] : door.DoorRef);
            var e = events.Emit(granted ? "access.granted" : "access.denied", door.DoorRef, card, holder, reader, request.Direction == "Out" ? "Out" : "In");
            return Results.Ok(new { decision = granted ? "granted" : "denied", eventId = e.Sequence });
        });
        app.MapGet("/dev/assignments", (CloudPacsStore store) => Results.Ok(store.AllAssignments().Select(a => new
        {
            a.AssignmentId,
            a.GroupId,
            a.ExternalRef,
            a.CardNumber,
            a.Active,
            a.AddedAt,
            a.RemovedAt,
            holderName = store.HolderName(a.ExternalRef),
        })));
    }
}
