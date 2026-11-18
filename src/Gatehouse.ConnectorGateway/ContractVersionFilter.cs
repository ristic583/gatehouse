using Gatehouse.Connector.Contracts;
using Microsoft.AspNetCore.Http;

namespace Gatehouse.ConnectorGateway;

public sealed class ContractVersionFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        if (http.Request.Headers[ConnectorHeaders.ContractVersion] != ConnectorHeaders.CurrentContractVersion)
        {
            return ValueTask.FromResult<object?>(ConnectorProblems.Create(
                http,
                "connector.contract_version_unsupported",
                400,
                "Contract version unsupported",
                $"Header {ConnectorHeaders.ContractVersion}: {ConnectorHeaders.CurrentContractVersion} is required."));
        }

        return next(context);
    }
}
