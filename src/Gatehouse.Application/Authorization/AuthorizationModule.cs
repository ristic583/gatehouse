using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Identity;
using Gatehouse.Application.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.Application.Authorization;

public static class AuthorizationModule
{
    public static IServiceCollection AddGatehouseAuthorization(this IServiceCollection services)
    {
        var tenantMember = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new TenantMemberRequirement())
            .Build();
        var linkedPerson = new AuthorizationPolicyBuilder()
            .Combine(tenantMember)
            .AddRequirements(new LinkedPersonRequirement())
            .Build();

        services.AddAuthorizationCore(options =>
        {
            options.AddPolicy(Policies.TenantMember, tenantMember);
            options.AddPolicy(Policies.LinkedPerson, linkedPerson);
            options.AddPolicy(Policies.TenantAdmin, p => p.Combine(linkedPerson).AddRequirements(new TenantAdminRequirement()));
            options.AddPolicy(Policies.SecurityStaff, p => p.Combine(linkedPerson).AddRequirements(new SecurityStaffRequirement()));
            options.AddPolicy(Policies.StaffConsole, p => p.Combine(linkedPerson).AddRequirements(new StaffConsoleRequirement()));
            options.AddPolicy(Policies.AuditReader, p => p.Combine(linkedPerson).AddRequirements(new AuditReaderRequirement()));
            options.AddPolicy(Policies.PlatformOperator, p => p
                .AddAuthenticationSchemes(PlatformAuthentication.Scheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new PlatformOperatorRequirement()));
            options.FallbackPolicy = tenantMember;
        });

        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserAccessor>());
        services.AddScoped<RoleResolver>();

        services.AddScoped<IAuthorizationHandler, TenantMemberHandler>();
        services.AddScoped<IAuthorizationHandler, LinkedPersonHandler>();
        services.AddScoped<IAuthorizationHandler, TenantAdminHandler>();
        services.AddScoped<IAuthorizationHandler, SecurityStaffScopeHandler>();
        services.AddScoped<IAuthorizationHandler, SelfHandler>();
        services.AddScoped<IAuthorizationHandler, SelfEmployeeHandler>();
        services.AddScoped<IAuthorizationHandler, PinRevealTaskHandler>();
        services.AddScoped<IAuthorizationHandler, TeamManagerHandler>();
        services.AddScoped<IAuthorizationHandler, SponsorHandler>();
        services.AddScoped<IAuthorizationHandler, AreaOwnerHandler>();
        services.AddScoped<IAuthorizationHandler, NoSelfApprovalHandler>();
        services.AddScoped<IAuthorizationHandler, NoSelfFulfillmentHandler>();
        services.AddScoped<IAuthorizationHandler, PlatformOperatorHandler>();

        return services;
    }
}
