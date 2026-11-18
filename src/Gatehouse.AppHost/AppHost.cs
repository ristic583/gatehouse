#pragma warning disable ASPIRECERTIFICATES001 // WithoutHttpsCertificate (Keycloak stays on HTTP) is experimental in Aspire 13.6.
using Gatehouse.AppHost;
using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

// Fixed host port: tenant connection strings are stored (encrypted) in the catalog and must stay valid across runs.
var postgres = builder.AddPostgres("postgres", port: 55432).WithDataVolume("gatehouse-postgres-data");
var catalog = postgres.AddDatabase("catalog");
var masterKey = builder.AddParameter(
    "secrets-master-key",
    new GenerateParameterDefault { MinLength = 48, Special = false },
    secret: true,
    persist: true);
var demoConnectorToken = builder.AddParameter(
    "demo-connector-token",
    new GenerateParameterDefault { MinLength = 40, Special = false },
    secret: true,
    persist: true);
var demoConnectorTokenAarhus = builder.AddParameter(
    "demo-connector-token-aarhus",
    new GenerateParameterDefault { MinLength = 40, Special = false },
    secret: true,
    persist: true);
var migrator = builder.AddProject<Projects.Gatehouse_Cli>("migrator")
    .WithArgs("bootstrap")
    .WithReference(postgres).WithReference(catalog)
    .WithEnvironment("Gatehouse__Secrets__MasterKey", masterKey)
    .WithEnvironment("Connector__DemoToken", demoConnectorToken)
    .WithEnvironment("Connector__DemoTokenAarhus", demoConnectorTokenAarhus)
    .WithEnvironment("Platform__BootstrapOperatorEmail", "operator@platform.test")
    .WaitFor(catalog);

var webClientSecret = builder.AddParameter(
    "web-client-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true);
var demoPassword = builder.AddParameter(
    "demo-user-password",
    new GenerateParameterDefault { MinLength = 16, Special = false },
    secret: true,
    persist: true);
var brokerSecret = builder.AddParameter(
    "broker-client-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true);
// M10 platform operator realm + Keycloak admin client used by tenant provisioning (ADR-009).
var provisionerSecret = builder.AddParameter(
    "provisioner-client-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true);
var platformWebClientSecret = builder.AddParameter(
    "platform-web-client-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true);
var platformOperatorPassword = builder.AddParameter(
    "platform-operator-password",
    new GenerateParameterDefault { MinLength = 16, Special = false },
    secret: true,
    persist: true);
var platformOperatorOtpSecret = builder.AddParameter(
    "platform-operator-otp-secret",
    new GenerateParameterDefault { MinLength = 20, Special = false },
    secret: true,
    persist: true);
var acmeBrokerSecret = builder.AddParameter(
    "acme-broker-client-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true);
const string PlatformAuthority = "http://localhost:8080/realms/gatehouse-platform";
const string PlatformAudience = "gatehouse-platform-api";

// Dev mail catcher: Keycloak (realm gatehouse) sends invitations and password mails here; UI http://localhost:8025.
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.27.0")
    .WithEndpoint(port: 1025, targetPort: 1025, name: "smtp", scheme: "tcp")
    .WithHttpEndpoint(port: 8025, targetPort: 8025, name: "http");

// Keycloak keeps its data in named volumes so user ids (OIDC `sub`) survive AppHost restarts; persons store the subject.
// Realm JSON is imported only when the realm does not exist yet: after changing infra/keycloak, remove the two volumes.
var upstreamIdp = builder.AddKeycloak("upstream-idp", 8081)
    .WithDataVolume("gatehouse-upstream-idp-data")
    .WithRealmImport("../../infra/keycloak/upstream")
    .WithEnvironment("GH_DEMO_USER_PASSWORD", demoPassword)
    .WithEnvironment("GH_BROKER_CLIENT_SECRET", brokerSecret)
    .WithEnvironment("GH_ACME_BROKER_CLIENT_SECRET", acmeBrokerSecret)
    .WithEnvironment("KC_HOSTNAME", "http://localhost:8081")
    .WithEnvironment("KC_HOSTNAME_BACKCHANNEL_DYNAMIC", "true")
    // Stay on plain HTTP even when a developer certificate exists (CI): the BFF and the realms use http://localhost:808x.
    .WithoutHttpsCertificate();
var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume("gatehouse-keycloak-data")
    .WithRealmImport("../../infra/keycloak/realms")
    .WithEnvironment("GH_WEB_CLIENT_SECRET", webClientSecret)
    .WithEnvironment("GH_BROKER_CLIENT_SECRET", brokerSecret)
    .WithEnvironment("UPSTREAM_IDP_INTERNAL_URL", upstreamIdp.GetEndpoint("http"))
    .WithEnvironment("GH_PROVISIONER_CLIENT_SECRET", provisionerSecret)
    .WithEnvironment("GH_PLATFORM_WEB_CLIENT_SECRET", platformWebClientSecret)
    .WithEnvironment("GH_PLATFORM_OPERATOR_PASSWORD", platformOperatorPassword)
    .WithEnvironment("GH_PLATFORM_OPERATOR_OTP_SECRET", platformOperatorOtpSecret)
    .WithoutHttpsCertificate()
    .WaitFor(upstreamIdp)
    .WaitFor(mailpit);

// The bootstrap links the demo tenants to their Keycloak organizations (suspension disables them), so it waits for Keycloak.
migrator
    .WithEnvironment("Keycloak__AdminUrl", "http://localhost:8080")
    .WithEnvironment("Keycloak__ProvisionerClientId", "gatehouse-provisioner")
    .WithEnvironment("Keycloak__ProvisionerClientSecret", provisionerSecret)
    .WaitFor(keycloak);

// S3-compatible object storage for photos (RustFS; MinIO community images are no longer published on Docker Hub).
var objectStoreUser = builder.AddParameter("object-store-user", "gatehouse-dev");
var objectStorePassword = builder.AddParameter(
    "object-store-password",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true);
var objectStore = builder.AddContainer("object-store", "rustfs/rustfs", "1.0.5")
    .WithEnvironment("RUSTFS_ACCESS_KEY", objectStoreUser)
    .WithEnvironment("RUSTFS_SECRET_KEY", objectStorePassword)
    .WithEnvironment("RUSTFS_CONSOLE_ENABLE", "false")
    .WithVolume("gatehouse-object-store-data", "/data")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "api");

var redis = builder.AddRedis("redis");
var rabbitmq = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin();

var hrFeedDirectory = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "infra", "hr-feed"));

var api = builder.AddProject<Projects.Gatehouse_Api>("api")
    .WithReference(postgres).WithReference(catalog).WithReference(redis).WithReference(rabbitmq)
    .WithReference(keycloak)
    .WithEnvironment("Gatehouse__Auth__Authority", "http://localhost:8080/realms/gatehouse")
    .WithEnvironment("Gatehouse__Auth__Audience", "gatehouse-api")
    .WithEnvironment("Platform__Authority", PlatformAuthority)
    .WithEnvironment("Platform__Audience", PlatformAudience)
    .WithEnvironment("Keycloak__AdminUrl", "http://localhost:8080")
    .WithEnvironment("Keycloak__ProvisionerClientId", "gatehouse-provisioner")
    .WithEnvironment("Keycloak__ProvisionerClientSecret", provisionerSecret)
    // Development only: the local upstream IdP (host and container address) is plain HTTP (M10 plan, Identity:AllowedInsecureHosts).
    .WithEnvironment("Identity__AllowedInsecureHosts__0", "localhost:8081")
    .WithEnvironment("Identity__AllowedInsecureHosts__1", "upstream-idp:8080")
    // Keycloak runs in a container: tenant IdP token/JWKS URLs on the host's localhost:8081 must use the container alias.
    .WithEnvironment("Keycloak__BackchannelRewrites__0", "http://localhost:8081=http://upstream-idp:8080")
    .WithEnvironment("Gatehouse__Secrets__MasterKey", masterKey)
    .WithEnvironment("Gatehouse__Dev__EnableReset", "true")
    .WithEnvironment("Connector__DemoToken", demoConnectorToken)
    .WithEnvironment("HrSync__FeedDirectory", hrFeedDirectory)
    .WithEnvironment("Storage__Endpoint", objectStore.GetEndpoint("api"))
    .WithEnvironment("Storage__AccessKey", objectStoreUser)
    .WithEnvironment("Storage__SecretKey", objectStorePassword)
    .WaitFor(objectStore)
    .WaitFor(catalog).WaitFor(redis).WaitFor(keycloak).WaitFor(rabbitmq)
    .WaitForCompletion(migrator);

builder.AddProject<Projects.Gatehouse_Workers>("workers")
    .WithReference(postgres).WithReference(catalog).WithReference(redis).WithReference(rabbitmq)
    .WithEnvironment("Keycloak__AdminUrl", "http://localhost:8080")
    .WithEnvironment("Keycloak__ProvisionerClientId", "gatehouse-provisioner")
    .WithEnvironment("Keycloak__ProvisionerClientSecret", provisionerSecret)
    .WithEnvironment("Gatehouse__Secrets__MasterKey", masterKey)
    .WithEnvironment("HrSync__FeedDirectory", hrFeedDirectory)
    .WithEnvironment("Storage__Endpoint", objectStore.GetEndpoint("api"))
    .WithEnvironment("Storage__AccessKey", objectStoreUser)
    .WithEnvironment("Storage__SecretKey", objectStorePassword)
    .WaitFor(objectStore)
    .WaitFor(catalog).WaitFor(redis).WaitFor(rabbitmq)
    .WaitForCompletion(migrator);

var gatewayCertificatePath = DevGatewayCertificate.EnsureCreated();
var gateway = builder.AddProject<Projects.Gatehouse_ConnectorGateway>("gateway", launchProfileName: null)
    .WithHttpsEndpoint(port: 5443, name: "https")
    .WithEnvironment("Kestrel__Certificates__Default__Path", gatewayCertificatePath)
    .WithHealthCheck("gateway-https")
    .WithReference(postgres).WithReference(catalog).WithReference(redis).WithReference(rabbitmq)
    .WithEnvironment("Gatehouse__Secrets__MasterKey", masterKey)
    .WaitFor(catalog).WaitFor(redis).WaitFor(api)
    .WaitForCompletion(migrator);

// The default HTTP health check rejects the self-signed gateway certificate; pin its thumbprint instead.
var gatewayHttps = gateway.GetEndpoint("https");
builder.Services.AddHealthChecks().AddAsyncCheck(
    "gateway-https", ct => DevGatewayCertificate.CheckHealthAsync(gatewayHttps, gatewayCertificatePath, ct));

var legacyPacsDb = postgres.AddDatabase("legacypacs");
var legacyPacsSim = builder.AddProject<Projects.LegacyPacs_Simulator>("legacypacs-sim", launchProfileName: null)
    .WithHttpEndpoint(port: 5180, name: "http")
    .WithReference(legacyPacsDb)
    .WaitFor(legacyPacsDb);

var agentDataDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gatehouse", "agents", "nordlys-hq-copenhagen");
builder.AddProject<Projects.Gatehouse_Agent>("agent-nordlys-hq", launchProfileName: null)
    .WithEnvironment("Agent__GatewayUrl", gateway.GetEndpoint("https"))
    .WithEnvironment("Agent__EnrollmentToken", demoConnectorToken)
    .WithEnvironment("Agent__DataDirectory", agentDataDirectory)
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithEnvironment("Agent__TrustGatewayCertificate", "dev")
    .WithEnvironment("Agent__LegacyPacsConnectionString", legacyPacsDb)
    .WaitFor(gateway).WaitFor(legacyPacsSim);

var cloudPacsSim = builder.AddProject<Projects.CloudPacs_Simulator>("cloudpacs-sim", launchProfileName: null)
    .WithHttpEndpoint(port: 5185, name: "http")
    .WithEnvironment("CloudPacs__ReadDelayMs", "300")
    .WithEnvironment("CloudPacs__ErrorRate", "0.02");

var aarhusAgentDataDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gatehouse", "agents", "nordlys-warehouse-aarhus");
builder.AddProject<Projects.Gatehouse_Agent>("agent-nordlys-aarhus", launchProfileName: null)
    .WithEnvironment("Agent__GatewayUrl", gateway.GetEndpoint("https"))
    .WithEnvironment("Agent__EnrollmentToken", demoConnectorTokenAarhus)
    .WithEnvironment("Agent__DataDirectory", aarhusAgentDataDirectory)
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithEnvironment("Agent__TrustGatewayCertificate", "dev")
    .WithEnvironment("Agent__Vendor", "CloudPacs")
    .WithEnvironment("Agent__CloudPacsUrl", cloudPacsSim.GetEndpoint("http"))
    .WithEnvironment("Agent__CloudPacsApiKey", "dev-cloudpacs-key")
    .WithEnvironment("Agent__WebhookListenUrl", "http://localhost:5186")
    .WaitFor(gateway).WaitFor(cloudPacsSim);

builder.AddProject<Projects.Building_Simulator>("building-sim", launchProfileName: null)
    .WithHttpEndpoint(port: 5190, name: "http")
    .WithEnvironment("Simulator__LegacyPacsUrl", legacyPacsSim.GetEndpoint("http"))
    .WithEnvironment("Simulator__CloudPacsUrl", cloudPacsSim.GetEndpoint("http"))
    .WaitFor(legacyPacsSim).WaitFor(cloudPacsSim);

builder.AddJavaScriptApp("web", "../web", "dev")
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WithReference(redis)
    .WithEnvironment("GATEHOUSE_OIDC_ISSUER", "http://localhost:8080/realms/gatehouse")
    .WithEnvironment("GATEHOUSE_OIDC_CLIENT_ID", "gatehouse-web")
    .WithEnvironment("GATEHOUSE_OIDC_CLIENT_SECRET", webClientSecret)
    .WithEnvironment("GATEHOUSE_PUBLIC_URL", "http://localhost:3000")
    .WithEnvironment("GATEHOUSE_PLATFORM_OIDC_ISSUER", PlatformAuthority)
    .WithEnvironment("GATEHOUSE_PLATFORM_OIDC_CLIENT_ID", "gatehouse-platform-web")
    .WithEnvironment("GATEHOUSE_PLATFORM_OIDC_CLIENT_SECRET", platformWebClientSecret)
    // One-click sign-in as a demo user on the login page (src/web/lib/auth/demo-sign-in.ts); the password stays on the server.
    .WithEnvironment("GATEHOUSE_DEMO_SIGN_IN", "true")
    .WithEnvironment("GATEHOUSE_DEMO_PASSWORD", demoPassword)
    .WaitFor(api)
    .WaitFor(keycloak);

builder.Build().Run();
