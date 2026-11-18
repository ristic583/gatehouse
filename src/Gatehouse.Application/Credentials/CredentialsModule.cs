using FluentValidation;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Credentials.ActivateCard;
using Gatehouse.Application.Credentials.AssignCard;
using Gatehouse.Application.Credentials.CardCommands;
using Gatehouse.Application.Credentials.DeactivateCard;
using Gatehouse.Application.Credentials.ListCards;
using Gatehouse.Application.Credentials.Photos;
using Gatehouse.Application.Credentials.Photos.DeleteMyPhoto;
using Gatehouse.Application.Credentials.Photos.GetPersonPhoto;
using Gatehouse.Application.Credentials.Photos.UploadMyPhoto;
using Gatehouse.Application.Credentials.RequestPinReveal;
using Gatehouse.Application.Credentials.RevealPin;
using Gatehouse.Application.Credentials.SetPin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatehouse.Application.Credentials;

public static class CredentialsModule
{
    /// <summary>Api: worker handlers plus the card/PIN endpoints' handlers.</summary>
    public static IServiceCollection AddCredentialsModule(this IServiceCollection services)
    {
        services.AddCredentialsWorkerHandlers();
        services.TryAddScoped<CardAccess>();
        services.TryAddScoped<CardReadModel>();
        services.TryAddScoped<ListMyCardsHandler>();
        services.TryAddScoped<ListPersonCardsHandler>();
        services.TryAddScoped<AssignCardHandler>();
        services.TryAddScoped<ActivateCardHandler>();
        services.TryAddScoped<DeactivateCardHandler>();
        services.TryAddScoped<SetPinHandler>();
        services.TryAddSingleton<IValidator<AssignCardRequest>, AssignCardValidator>();
        services.TryAddSingleton<IValidator<DeactivateCardRequest>, DeactivateCardValidator>();
        services.TryAddSingleton<IValidator<SetPinRequest>, SetPinValidator>();
        services.TryAddScoped<RequestPinRevealHandler>();
        services.TryAddScoped<RevealPinHandler>();
        services.TryAddScoped<UploadMyPhotoHandler>();
        services.TryAddScoped<DeleteMyPhotoHandler>();
        services.TryAddScoped<GetPersonPhotoHandler>();
        return services;
    }

    /// <summary>Handlers the Workers host needs (no current user or authorization service there).</summary>
    public static IServiceCollection AddCredentialsWorkerHandlers(this IServiceCollection services)
    {
        services.TryAddScoped<ConnectorCommandFactory>();
        services.TryAddScoped<CardCommandTargets>();
        services.TryAddScoped<CardCommandService>();
        services.TryAddScoped<ApplyCardCommandAckHandler>();
        return services;
    }
}
