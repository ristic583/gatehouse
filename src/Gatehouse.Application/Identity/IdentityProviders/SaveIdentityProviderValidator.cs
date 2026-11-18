using FluentValidation;
using Gatehouse.Application.Common.Validation;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Identity.IdentityProviders;

public sealed class SaveIdentityProviderValidator : AbstractValidator<SaveIdentityProviderRequest>, IValidationProblemCode
{
    public string ProblemCode => "idp.invalid_config";

    public SaveIdentityProviderValidator()
    {
        RuleFor(r => r.Kind).Must(k => k is IdpKind.Oidc or IdpKind.Saml).WithMessage("Kind must be Oidc or Saml.");
        RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(100);
        When(r => r.Kind == IdpKind.Oidc, () =>
        {
            RuleFor(r => r.Oidc).NotNull();
            RuleFor(r => r.Oidc!.DiscoveryUrl).NotEmpty().MaximumLength(2000)
                .Must(u => Uri.TryCreate(u, UriKind.Absolute, out _)).WithMessage("Discovery URL must be absolute.")
                .When(r => r.Oidc is not null);
            RuleFor(r => r.Oidc!.ClientId).NotEmpty().MaximumLength(255).When(r => r.Oidc is not null);
            RuleFor(r => r.Oidc!.ClientSecret).MaximumLength(2000).When(r => r.Oidc is not null);
            RuleForEach(r => r.Oidc!.Scopes).NotEmpty().MaximumLength(100).Matches("^[\\x21-\\x7E]+$").When(r => r.Oidc is not null);
            RuleFor(r => r.Saml).Null();
        });
        When(r => r.Kind == IdpKind.Saml, () =>
        {
            RuleFor(r => r.Saml).NotNull();
            RuleFor(r => r.Saml).Must(s => string.IsNullOrWhiteSpace(s!.MetadataUrl) != string.IsNullOrWhiteSpace(s.MetadataXml))
                .WithMessage("Set exactly one of metadataUrl and metadataXml.").When(r => r.Saml is not null);
            RuleFor(r => r.Saml!.MetadataUrl).Must(u => Uri.TryCreate(u, UriKind.Absolute, out _))
                .WithMessage("Metadata URL must be absolute.")
                .When(r => !string.IsNullOrWhiteSpace(r.Saml?.MetadataUrl));
            RuleFor(r => r.Saml!.MetadataXml).MaximumLength(1024 * 1024).When(r => r.Saml is not null);
            RuleFor(r => r.Oidc).Null();
        });
    }
}
