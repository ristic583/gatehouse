# Every .NET image of the demo stack (deploy/demo/compose.yml). One build stage publishes all of them; each target is one image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Gatehouse.Api -c Release -o /out/api -p:NuGetAudit=false \
 && dotnet publish src/Gatehouse.Workers -c Release -o /out/workers -p:NuGetAudit=false \
 && dotnet publish src/Gatehouse.ConnectorGateway -c Release -o /out/gateway -p:NuGetAudit=false \
 && dotnet publish src/Gatehouse.Cli -c Release -o /out/cli -p:NuGetAudit=false \
 && dotnet publish src/Gatehouse.Agent -c Release -o /out/agent -p:NuGetAudit=false \
 && dotnet publish simulators/LegacyPacs.Simulator -c Release -o /out/legacypacs -p:NuGetAudit=false \
 && dotnet publish simulators/CloudPacs.Simulator -c Release -o /out/cloudpacs -p:NuGetAudit=false \
 && dotnet publish simulators/Building.Simulator -c Release -o /out/building -p:NuGetAudit=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ENV ASPNETCORE_ENVIRONMENT=Production DOTNET_ENVIRONMENT=Production DOTNET_gcServer=0
WORKDIR /app

FROM runtime AS api
COPY --from=build /out/api .
USER app
ENTRYPOINT ["dotnet", "Gatehouse.Api.dll"]

FROM runtime AS workers
COPY --from=build /out/workers .
USER app
ENTRYPOINT ["dotnet", "Gatehouse.Workers.dll"]

FROM runtime AS gateway
COPY --from=build /out/gateway .
USER app
ENTRYPOINT ["dotnet", "Gatehouse.ConnectorGateway.dll"]

FROM runtime AS cli
COPY --from=build /out/cli .
USER app
ENTRYPOINT ["dotnet", "gatehouse-cli.dll"]

FROM runtime AS agent
ENV Agent__DataDirectory=/data
RUN mkdir -p /data && chown app:app /data
COPY --from=build /out/agent .
USER app
VOLUME ["/data"]
ENTRYPOINT ["dotnet", "Gatehouse.Agent.dll"]

FROM runtime AS legacypacs-sim
ENV ASPNETCORE_URLS=http://+:5180
COPY --from=build /out/legacypacs .
USER app
ENTRYPOINT ["dotnet", "LegacyPacs.Simulator.dll"]

FROM runtime AS cloudpacs-sim
ENV ASPNETCORE_URLS=http://+:5185
COPY --from=build /out/cloudpacs .
USER app
ENTRYPOINT ["dotnet", "CloudPacs.Simulator.dll"]

FROM runtime AS building-sim
ENV ASPNETCORE_URLS=http://+:5190
COPY --from=build /out/building .
USER app
ENTRYPOINT ["dotnet", "Building.Simulator.dll"]
