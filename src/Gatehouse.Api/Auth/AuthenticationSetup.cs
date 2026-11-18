using Gatehouse.Application.Platform;
using Gatehouse.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gatehouse.Api.Auth;

/// <summary>
/// Default scheme <see cref="DefaultScheme"/> forwards by the bearer token's <c>iss</c>: platform guest tokens (<c>gatehouse-guest</c>)
/// to <see cref="GuestScheme"/>, everything else to the Keycloak JwtBearer scheme.
/// </summary>
public static class AuthenticationSetup
{
    public const string AuthoritySetting = "Gatehouse:Auth:Authority";
    public const string AudienceSetting = "Gatehouse:Auth:Audience";
    public const string DefaultScheme = "Gatehouse";
    public const string GuestScheme = "Guest";
    public const string PlatformScheme = PlatformAuthentication.Scheme;
    public const string PlatformAuthoritySetting = "Platform:Authority";

    /// <summary>
    /// Optional OIDC metadata URL on an internal network (deployment: Keycloak in the same Docker network). The token issuer stays
    /// <see cref="AuthoritySetting"/>; only metadata and keys are read from here, so server-to-server calls skip the public proxy.
    /// </summary>
    public const string MetadataAddressSetting = "Gatehouse:Auth:MetadataAddress";

    public const string PlatformMetadataAddressSetting = "Platform:MetadataAddress";
    public const string PlatformAudienceSetting = "Platform:Audience";

    public static IHostApplicationBuilder AddGatehouseAuthentication(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddAuthentication(DefaultScheme)
            .AddPolicyScheme(DefaultScheme, DefaultScheme, options => options.ForwardDefaultSelector = SelectScheme)
            .AddJwtBearer()
            .AddJwtBearer(GuestScheme)
            .AddJwtBearer(PlatformScheme);

        builder.Services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfiguration, IHostEnvironment>((options, configuration, environment) =>
            {
                options.Authority = configuration[AuthoritySetting];
                options.Audience = configuration[AudienceSetting];
                ApplyMetadataAddress(options, configuration[MetadataAddressSetting], environment);
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "email";
            });

        // Platform operators (ADR-009 §1): realm gatehouse-platform only. Never selected by the default policy scheme,
        // so a platform token on a tenant route is rejected by the tenant JwtBearer scheme (401).
        builder.Services
            .AddOptions<JwtBearerOptions>(PlatformScheme)
            .Configure<IConfiguration, IHostEnvironment>((options, configuration, environment) =>
            {
                options.Authority = configuration[PlatformAuthoritySetting];
                options.Audience = configuration[PlatformAudienceSetting];
                ApplyMetadataAddress(options, configuration[PlatformMetadataAddressSetting], environment);
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "email";
                options.TokenValidationParameters.AuthenticationType = PlatformScheme;
            });

        builder.Services
            .AddOptions<JwtBearerOptions>(GuestScheme)
            .Configure<GuestTokenIssuer>((options, issuer) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = GuestTokenIssuer.Issuer,
                    ValidAudience = GuestTokenIssuer.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(issuer.SigningKey),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "email",
                };
            });

        return builder;
    }

    private static string SelectScheme(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = header["Bearer ".Length..].Trim();
            var handler = new JsonWebTokenHandler();
            if (handler.CanReadToken(token))
            {
                try
                {
                    if (handler.ReadJsonWebToken(token).Issuer == GuestTokenIssuer.Issuer)
                    {
                        return GuestScheme;
                    }
                }
                catch (ArgumentException)
                {
                    // Unreadable token: let the Keycloak scheme reject it.
                }
            }
        }

        return JwtBearerDefaults.AuthenticationScheme;
    }

    private static void ApplyMetadataAddress(JwtBearerOptions options, string? metadataAddress, IHostEnvironment environment)
    {
        options.RequireHttpsMetadata = !environment.IsDevelopment();
        if (string.IsNullOrWhiteSpace(metadataAddress))
        {
            return;
        }

        options.MetadataAddress = metadataAddress;
        // An internal metadata address is plain HTTP inside the container network; the public issuer stays HTTPS.
        options.RequireHttpsMetadata = metadataAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }
}
