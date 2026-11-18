using FluentValidation;
using Gatehouse.Application.Access.Renewal;
using Gatehouse.Application.Fulfillment;
using Gatehouse.Application.People.Export;
using Gatehouse.Application.People.GetPerson;
using Gatehouse.Application.People.HrSync;
using Gatehouse.Application.People.Lifecycle;
using Gatehouse.Application.People.ListPeople;
using Gatehouse.Application.People.OnboardPerson;
using Gatehouse.Application.People.Retention;
using Gatehouse.Application.People.Sponsorship;
using Gatehouse.Application.People.Termination;
using Gatehouse.Application.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatehouse.Application.People;

public static class PeopleModule
{
    /// <summary>Api: people list, detail and onboarding (needs <c>AddCredentialsModule</c> for cards).</summary>
    public static IServiceCollection AddPeopleModule(this IServiceCollection services)
    {
        services.AddPeopleWorkerHandlers();
        services.TryAddScoped<PersonSummaries>();
        services.TryAddScoped<PersonDetails>();
        services.TryAddScoped<ListPeopleHandler>();
        services.TryAddScoped<GetPersonHandler>();
        services.TryAddScoped<OnboardPersonHandler>();
        services.TryAddScoped<Admin.PeopleAdminAudit>();
        services.TryAddScoped<Admin.CreateEmployee.CreateEmployeeHandler>();
        services.TryAddScoped<Admin.ImportPeople.ImportPeopleHandler>();
        services.TryAddScoped<Admin.ListRoleAssignments.ListRoleAssignmentsHandler>();
        services.TryAddScoped<Admin.AssignRole.AssignRoleHandler>();
        services.TryAddSingleton<IValidator<Dtos.CreateEmployeeRequest>, Admin.CreateEmployee.CreateEmployeeValidator>();
        services.TryAddSingleton<IValidator<Dtos.AssignRoleRequest>, Admin.AssignRole.AssignRoleValidator>();
        services.TryAddScoped<Admin.RemoveRoleAssignment.RemoveRoleAssignmentHandler>();
        services.TryAddSingleton<IValidator<OnboardPersonCommand>, OnboardPersonValidator>();
        services.TryAddScoped<ManagedPersonLoader>();
        services.TryAddScoped<ExtendSponsorshipHandler>();
        services.TryAddScoped<EndSponsorshipHandler>();
        services.TryAddScoped<TerminatePersonHandler>();
        services.TryAddScoped<LifecycleAdminHandler>();
        services.TryAddScoped<HrSyncAdminHandler>();
        services.TryAddScoped<RetentionAdminHandler>();
        services.TryAddScoped<PersonExportHandler>();
        return services;
    }

    /// <summary>Services the Workers host needs (no current user or authorization service there).</summary>
    public static IServiceCollection AddPeopleWorkerHandlers(this IServiceCollection services)
    {
        services.TryAddScoped<LifecycleSettingsStore>();
        services.TryAddScoped<StaffTaskService>();
        services.TryAddScoped<FulfillmentPlanner>();
        services.TryAddScoped<TerminationService>();
        services.TryAddScoped<RenewalScheduler>();
        services.TryAddScoped<Access.Expiry.UnapprovedRequestExpiry>();
        services.TryAddScoped<LifecycleRunner>();
        services.TryAddScoped<HrSyncRunner>();
        services.TryAddScoped<RetentionSettingsStore>();
        services.TryAddScoped<RetentionRunner>();
        return services;
    }
}
