using FluentValidation;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.CreateArea;
using Gatehouse.Application.Sites.CreateDoor;
using Gatehouse.Application.Sites.CreateFloor;
using Gatehouse.Application.Sites.CreateReader;
using Gatehouse.Application.Sites.CreateSite;
using Gatehouse.Application.Sites.DeleteArea;
using Gatehouse.Application.Sites.DeleteDoor;
using Gatehouse.Application.Sites.DeleteFloor;
using Gatehouse.Application.Sites.DeleteReader;
using Gatehouse.Application.Sites.DeleteSite;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Application.Sites.GetArea;
using Gatehouse.Application.Sites.GetFloorBackground;
using Gatehouse.Application.Sites.ListSecurityLevels;
using Gatehouse.Application.Sites.ListSites;
using Gatehouse.Application.Sites.RemoveFloorBackground;
using Gatehouse.Application.Sites.SetAreaOwners;
using Gatehouse.Application.Sites.UpdateArea;
using Gatehouse.Application.Sites.UpdateDoor;
using Gatehouse.Application.Sites.UpdateFloor;
using Gatehouse.Application.Sites.UpdateReader;
using Gatehouse.Application.Sites.UpdateSite;
using Gatehouse.Application.Sites.UploadFloorBackground;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.Application.Sites;

public static class SitesModule
{
    public static IServiceCollection AddSitesModule(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<SaveSiteRequest>, SaveSiteValidator>();
        services.AddSingleton<IValidator<SaveFloorRequest>, SaveFloorValidator>();
        services.AddSingleton<IValidator<SaveAreaRequest>, SaveAreaValidator>();
        services.AddSingleton<IValidator<SaveDoorRequest>, SaveDoorValidator>();
        services.AddSingleton<IValidator<SaveReaderRequest>, SaveReaderValidator>();
        services.AddScoped<ListSitesHandler>();
        services.AddScoped<GetAreaHandler>();
        services.AddScoped<AdminAudit>();
        services.AddScoped<FloorPlanReader>();
        services.AddScoped<ListSecurityLevelsHandler>();
        services.AddScoped<CreateSiteHandler>();
        services.AddScoped<UpdateSiteHandler>();
        services.AddScoped<DeleteSiteHandler>();
        services.AddScoped<CreateFloorHandler>();
        services.AddScoped<UpdateFloorHandler>();
        services.AddScoped<DeleteFloorHandler>();
        services.AddScoped<CreateAreaHandler>();
        services.AddScoped<UpdateAreaHandler>();
        services.AddScoped<SetAreaOwnersHandler>();
        services.AddScoped<DeleteAreaHandler>();
        services.AddScoped<CreateDoorHandler>();
        services.AddScoped<UpdateDoorHandler>();
        services.AddScoped<DeleteDoorHandler>();
        services.AddScoped<CreateReaderHandler>();
        services.AddScoped<UpdateReaderHandler>();
        services.AddScoped<DeleteReaderHandler>();
        services.AddScoped<UploadFloorBackgroundHandler>();
        services.AddScoped<RemoveFloorBackgroundHandler>();
        services.AddScoped<GetFloorBackgroundHandler>();
        return services;
    }
}
