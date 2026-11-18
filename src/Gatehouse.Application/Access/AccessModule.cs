using FluentValidation;
using Gatehouse.Application.Access.AccessMap;
using Gatehouse.Application.Access.Approvals;
using Gatehouse.Application.Access.AreaRoster;
using Gatehouse.Application.Access.CancelAccessRequest;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Access.GetAccessRequest;
using Gatehouse.Application.Access.ListAccessRequests;
using Gatehouse.Application.Access.RequestAccess;
using Gatehouse.Application.Access.Revocation;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.Application.Access;

public static class AccessModule
{
    public static IServiceCollection AddAccessModule(this IServiceCollection services)
    {
        services.AddScoped<GetFloorAccessMapHandler>();
        services.AddScoped<AccessRequestDtoBuilder>();
        services.AddScoped<EntitlementResourceLoader>();
        services.AddScoped<RequestAccessHandler>();
        services.AddScoped<ListAccessRequestsHandler>();
        services.AddScoped<GetAccessRequestHandler>();
        services.AddScoped<StepDecisionService>();
        services.AddScoped<GetApprovalInboxHandler>();
        services.AddScoped<ApproveStepHandler>();
        services.AddScoped<RejectStepHandler>();
        services.AddScoped<GetAreaRosterHandler>();
        services.AddScoped<RevocationService>();
        services.AddScoped<RevokeEntitlementHandler>();
        services.AddSingleton<IValidator<RevokeEntitlementDto>, RevokeEntitlementValidator>();
        services.AddScoped<EmergencyRevokeHandler>();
        services.AddScoped<CancelAccessRequestHandler>();
        services.AddSingleton<IValidator<RequestAccessCommand>, RequestAccessValidator>();
        return services;
    }
}
