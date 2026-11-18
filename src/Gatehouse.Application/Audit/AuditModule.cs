using Gatehouse.Application.Audit.ListAuditEntries;
using Gatehouse.Application.Audit.VerifyAuditChain;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.Application.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<VerifyAuditChainHandler>();
        services.AddScoped<ListAuditEntriesHandler>();
        return services;
    }
}
