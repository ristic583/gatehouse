using FluentValidation;
using Gatehouse.Application.Fulfillment.PacsAdmin.CreatePacsInstance;
using Gatehouse.Application.Fulfillment.PacsAdmin.DeletePacsInstance;
using Gatehouse.Application.Fulfillment.PacsAdmin.GetPacsInstance;
using Gatehouse.Application.Fulfillment.PacsAdmin.ListPacsInstances;
using Gatehouse.Application.Fulfillment.PacsAdmin.RegisterConnector;
using Gatehouse.Application.Fulfillment.PacsAdmin.RevokeConnector;
using Gatehouse.Application.Fulfillment.PacsAdmin.SetAccessLevelMappings;
using Gatehouse.Application.Fulfillment.PacsAdmin.UpdatePacsInstance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatehouse.Application.Fulfillment.PacsAdmin;

public static class PacsAdminModule
{
    /// <summary>Tenant-admin PACS handlers; needs an <c>IConnectorRegistrar</c> implementation.</summary>
    public static IServiceCollection AddPacsAdminModule(this IServiceCollection services)
    {
        services.TryAddScoped<PacsAdminAudit>();
        services.TryAddScoped<PacsInstanceAdminReadModel>();
        services.TryAddScoped<ListPacsInstancesHandler>();
        services.TryAddScoped<GetPacsInstanceHandler>();
        services.TryAddScoped<CreatePacsInstanceHandler>();
        services.TryAddScoped<UpdatePacsInstanceHandler>();
        services.TryAddScoped<DeletePacsInstanceHandler>();
        services.TryAddScoped<SetAccessLevelMappingsHandler>();
        services.TryAddSingleton<IValidator<SavePacsInstanceRequest>, SavePacsInstanceValidator>();
        services.TryAddSingleton<IValidator<List<AccessLevelMappingRequest>>, SetAccessLevelMappingsValidator>();
        services.TryAddScoped<RegisterConnectorHandler>();
        services.TryAddScoped<RevokeConnectorHandler>();
        services.TryAddScoped<GrantMigrationService>();
        return services;
    }
}
