using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Gatehouse.Api.Auth;
using Gatehouse.Api.Features.Access;
using Gatehouse.Api.Features.Audit;
using Gatehouse.Api.Features.Credentials;
using Gatehouse.Api.Features.Dashboard;
using Gatehouse.Api.Features.Dev;
using Gatehouse.Api.Features.Fulfillment;
using Gatehouse.Api.Features.Identity;
using Gatehouse.Api.Features.LiveBuilding;
using Gatehouse.Api.Features.People;
using Gatehouse.Api.Features.Platform;
using Gatehouse.Api.Features.Reconciliation;
using Gatehouse.Api.Features.Sites;
using Gatehouse.Api.Features.Tasks;
using Gatehouse.Api.Features.Theming;
using Gatehouse.Api.Http;
using Gatehouse.Api.Tenancy;
using Gatehouse.Application.Access;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Credentials;
using Gatehouse.Application.Fulfillment;
using Gatehouse.Application.Fulfillment.PacsAdmin;
using Gatehouse.Application.Identity.IdentityProviders;
using Gatehouse.Application.Identity.MagicLinks;
using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.People;
using Gatehouse.Application.Reconciliation;
using Gatehouse.Application.Sites;
using Gatehouse.Application.Tasks;
using Gatehouse.Application.Tenancy.Onboarding;
using Gatehouse.Application.Theming;
using Gatehouse.Infrastructure;
using Gatehouse.Infrastructure.Identity.Keycloak;
using Gatehouse.Infrastructure.LiveBuilding;
using Gatehouse.Infrastructure.Platform;
using Gatehouse.Infrastructure.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("redis")))
{
    builder.AddRedisClient("redis");
}

builder.AddGatehouseCatalog();
builder.Services.AddGatehousePlatform();
builder.AddGatehouseTenancy();
builder.Services.AddTenantStatusChannel();
builder.AddGatehouseAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddGatehouseAuthorization();
builder.Services.AddSitesModule();
builder.Services.AddAccessModule();
builder.Services.AddAuditModule();
builder.Services.AddFulfillmentModule();
builder.Services.AddTasksModule();
builder.Services.AddLiveBuildingInfrastructure();
builder.Services.AddLiveBuildingModule();
builder.Services.AddReconciliationModule();
builder.Services.AddCredentialsModule();
builder.Services.AddPeopleModule();
builder.Services.AddMagicLinkHandlers();
builder.Services.AddThemingModule();
builder.Services.AddIdentityProviderHandlers();
builder.Services.AddPacsAdminModule();
builder.Services.AddOnboardingModule();
builder.Services.AddScoped<Gatehouse.Application.Connectors.IConnectorRegistrar>(
    sp => sp.GetRequiredService<Gatehouse.Infrastructure.Connectors.ConnectorRegistrationService>());
builder.Services.AddIdentityProviderInfrastructure();
builder.Services.AddScoped<Gatehouse.Application.Dashboard.GetDashboardHandler>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(MagicLinkEndpoints.GuestSessionRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR().AddJsonProtocol(o =>
{
    o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddHostedService<LiveUpdateRelay>();
builder.Services.AddSingleton<ILiveSiteAccessChecker, LiveSiteAccessChecker>();
builder.Services.AddSingleton<LiveSubscriptionRegistry>();
builder.Services.AddHostedService<LiveSubscriptionRevalidator>();
builder.Services
    .AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions.TryAdd(
            "traceId",
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);

        if (context.Exception is not null)
        {
            context.ProblemDetails.Title = "An unexpected error occurred.";
            context.ProblemDetails.Detail = null;
            context.ProblemDetails.Extensions["code"] = "server.unexpected";
        }
    };
});

var app = builder.Build();

// SEC-03: defence in depth when the API is reached directly (browsers normally go through the BFF). Set on starting so that error
// responses written by the exception handler keep them.
app.Use((context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "DENY";
        return Task.CompletedTask;
    });
    return next(context);
});

// SEC-04: HSTS on https responses outside Development (UseHsts skips localhost and plain http).
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// The built-in exception handler logs unhandled exceptions at Error and writes a 500 problem response.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<CurrentUserMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapDefaultEndpoints();
app.MapGet("/", () => "Gatehouse API").AllowAnonymous();
var api = app.MapGroup(string.Empty).WithRequestValidation();
api.MapIdentityEndpoints();
api.MapSitesEndpoints();
api.MapSitesAdminEndpoints();
api.MapAccessEndpoints();
api.MapAuditEndpoints();
api.MapFulfillmentEndpoints();
api.MapPacsAdminEndpoints();
api.MapTasksEndpoints();
api.MapLiveBuildingEndpoints();
api.MapReconciliationEndpoints();
api.MapCredentialsEndpoints();
api.MapPeopleEndpoints();
api.MapPeopleAdminEndpoints();
api.MapAdminLifecycleEndpoints();
api.MapMagicLinkEndpoints();
api.MapThemingEndpoints();
api.MapDashboardEndpoints();
api.MapPlatformEndpoints();
api.MapIdentityProviderEndpoints();
api.MapOnboardingEndpoints();
app.MapHub<LiveBuildingHub>(LiveBuildingHub.Path).RequireAuthorization(Policies.StaffConsole);
app.MapDevEndpoints(app.Configuration, app.Environment);

app.Run();

public partial class Program;
