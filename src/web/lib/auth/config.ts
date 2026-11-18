import "server-only";

export interface AuthConfig {
  issuer: string;
  /** Optional OIDC discovery URL on an internal network (deployment); the issuer in it must still equal `issuer`. */
  discoveryUrl?: string;
  clientId: string;
  clientSecret: string;
  publicUrl: string;
  redisConnectionString: string;
  apiBaseUrl: string;
}

function required(name: string): string {
  const value = process.env[name];
  if (value === undefined || value === "") {
    throw new Error(`Environment variable ${name} is not set.`);
  }
  return value;
}

function optional(name: string): string | undefined {
  const value = process.env[name];
  return value === undefined || value === "" ? undefined : value;
}

let cached: AuthConfig | undefined;

/** Server-only configuration for the BFF. Throws when a required setting is missing. */
export function getAuthConfig(): AuthConfig {
  if (cached !== undefined) {
    return cached;
  }

  const apiBaseUrl = process.env.services__api__https__0 ?? process.env.services__api__http__0;
  if (apiBaseUrl === undefined || apiBaseUrl === "") {
    throw new Error("Environment variable services__api__https__0 or services__api__http__0 is not set.");
  }

  cached = {
    issuer: required("GATEHOUSE_OIDC_ISSUER"),
    discoveryUrl: optional("GATEHOUSE_OIDC_DISCOVERY_URL"),
    clientId: required("GATEHOUSE_OIDC_CLIENT_ID"),
    clientSecret: required("GATEHOUSE_OIDC_CLIENT_SECRET"),
    publicUrl: required("GATEHOUSE_PUBLIC_URL").replace(/\/+$/, ""),
    redisConnectionString: required("ConnectionStrings__redis"),
    apiBaseUrl: apiBaseUrl.replace(/\/+$/, ""),
  };
  return cached;
}

let cachedPlatform: AuthConfig | undefined;

/** Server-only configuration of the platform operator BFF client (`gatehouse-platform-web`, ADR-009 §1). */
export function getPlatformAuthConfig(): AuthConfig {
  if (cachedPlatform !== undefined) {
    return cachedPlatform;
  }
  const tenant = getAuthConfig();
  cachedPlatform = {
    ...tenant,
    issuer: required("GATEHOUSE_PLATFORM_OIDC_ISSUER"),
    discoveryUrl: optional("GATEHOUSE_PLATFORM_OIDC_DISCOVERY_URL"),
    clientId: required("GATEHOUSE_PLATFORM_OIDC_CLIENT_ID"),
    clientSecret: required("GATEHOUSE_PLATFORM_OIDC_CLIENT_SECRET"),
  };
  return cachedPlatform;
}
