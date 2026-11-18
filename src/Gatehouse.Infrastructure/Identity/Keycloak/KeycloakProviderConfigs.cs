using System.Text.Json.Nodes;
using Gatehouse.Application.Tenancy.IdentityProvisioning;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Infrastructure.Identity.Keycloak;

/// <summary>Builds the Keycloak identity provider <c>config</c> objects and attribute mappers for OIDC and SAML upstreams.</summary>
internal sealed class KeycloakProviderConfigs(KeycloakOptions options)
{
    public const string MaskedSecret = "**********";
    private static readonly string[] SamlProfileAttributes = ["email", "firstName", "lastName"];

    private string SpEntityId => $"{options.PublicBase}/realms/{options.Realm}";

    public JsonObject Oidc(OidcProviderSpec spec)
    {
        var config = new JsonObject
        {
            ["issuer"] = spec.Issuer,
            ["authorizationUrl"] = spec.AuthorizationUrl,
            ["tokenUrl"] = Backchannel(spec.TokenUrl),
            ["jwksUrl"] = Backchannel(spec.JwksUrl),
            ["useJwksUrl"] = "true",
            ["validateSignature"] = "true",
            ["clientId"] = spec.ClientId,
            ["clientAuthMethod"] = "client_secret_post",
            ["pkceEnabled"] = "true",
            ["pkceMethod"] = "S256",
            ["syncMode"] = "FORCE",
            ["defaultScope"] = string.Join(' ', spec.Scopes.Count > 0 ? spec.Scopes : ["openid", "profile", "email"]),
        };
        if (spec.UserInfoUrl is { Length: > 0 } userInfo)
        {
            config["userInfoUrl"] = Backchannel(userInfo);
        }

        // Keycloak keeps the stored secret when it receives its own mask back.
        config["clientSecret"] = spec.ClientSecret ?? MaskedSecret;
        return config;
    }

    public JsonObject Saml(SamlProviderSpec spec) => new()
    {
        ["idpEntityId"] = spec.EntityId,
        ["singleSignOnServiceUrl"] = spec.SsoUrl,
        ["entityId"] = SpEntityId,
        ["signingCertificate"] = PemBody(spec.SigningCertificatePem),
        ["nameIDPolicyFormat"] = "urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress",
        ["principalType"] = "SUBJECT",
        ["postBindingResponse"] = "true",
        ["postBindingAuthnRequest"] = "true",
        ["postBindingLogout"] = "true",
        // Never relaxed through the API (ADR-009 §4).
        ["validateSignature"] = "true",
        ["wantAssertionsSigned"] = "true",
        ["wantAuthnRequestsSigned"] = "false",
        ["syncMode"] = "FORCE",
    };

    public ProvisionedProvider Provider(string alias, IdpKind kind) =>
        new(
            alias,
            kind,
            $"{options.PublicBase}/realms/{options.Realm}/broker/{alias}/endpoint",
            kind == IdpKind.Saml ? SpEntityId : null);

    public static List<JsonObject> OidcMappers(TenantId tenantId) => [TenantMapper(tenantId)];

    public static List<JsonObject> SamlMappers(TenantId tenantId) =>
    [
        TenantMapper(tenantId),
        .. SamlProfileAttributes.Select(a => Mapper(
            a,
            "saml-user-attribute-idp-mapper",
            ("attribute.name", a),
            ("user.attribute", a),
            ("attribute.name.format", "ATTRIBUTE_FORMAT_BASIC"))),
    ];

    /// <summary>
    /// Keycloak calls token, JWKS and userinfo endpoints server-to-server; <c>Keycloak:BackchannelRewrites</c> (<c>from=to</c>
    /// URL prefixes, Development) maps a browser-facing host to the one Keycloak can reach. The authorization URL stays as is.
    /// </summary>
    private string Backchannel(string url)
    {
        foreach (var rule in options.BackchannelRewrites)
        {
            var separator = rule.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && url.StartsWith(rule[..separator], StringComparison.OrdinalIgnoreCase))
            {
                return string.Concat(rule.AsSpan(separator + 1), url.AsSpan(separator));
            }
        }

        return url;
    }

    private static JsonObject Mapper(string name, string type, params (string Key, string Value)[] config)
    {
        var c = new JsonObject { ["syncMode"] = "FORCE" };
        foreach (var (key, value) in config)
        {
            c[key] = value;
        }

        return new JsonObject { ["name"] = name, ["identityProviderMapper"] = type, ["config"] = c };
    }

    private static JsonObject TenantMapper(TenantId tenantId) =>
        Mapper(
            KeycloakAdminClient.TenantAttribute,
            "hardcoded-attribute-idp-mapper",
            ("attribute", KeycloakAdminClient.TenantAttribute),
            ("attribute.value", tenantId.Value));

    private static string PemBody(string pem) => string.Concat(
        pem.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("-----", StringComparison.Ordinal)));
}
