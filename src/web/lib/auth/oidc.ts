import "server-only";
import * as client from "openid-client";
import { type AuthConfig, getAuthConfig, getPlatformAuthConfig } from "./config";
import { loginBindingMatches, newLoginBinding } from "./login-binding";
import type { PlatformSessionData } from "./platform-session";
import type { SessionData } from "./session";
import { getAuthTxStore, getPlatformAuthTxStore } from "./stores";

/** Which BFF client: the tenant realm (`gatehouse-web`) or the platform operator realm (`gatehouse-platform-web`). */
export type OidcClientKind = "tenant" | "platform";

function configFor(kind: OidcClientKind): AuthConfig {
  return kind === "platform" ? getPlatformAuthConfig() : getAuthConfig();
}

const AUTH_TX_TTL_SECONDS = 600;
const REFRESH_MARGIN_SECONDS = 60;

const configurationPromises = new Map<OidcClientKind, Promise<client.Configuration>>();

function getConfiguration(kind: OidcClientKind = "tenant"): Promise<client.Configuration> {
  let configurationPromise = configurationPromises.get(kind);
  if (configurationPromise === undefined) {
    const config = configFor(kind);
    const insecure = config.issuer.startsWith("http://");
    configurationPromise = (
      config.discoveryUrl !== undefined
        ? internalDiscovery(config)
        : client.discovery(
            new URL(config.issuer),
            config.clientId,
            config.clientSecret,
            undefined,
            insecure ? { execute: [client.allowInsecureRequests] } : undefined,
          )
    ).catch((error: unknown) => {
      configurationPromises.delete(kind);
      throw error;
    });
    configurationPromises.set(kind, configurationPromise);
  }
  return configurationPromise;
}

/**
 * Deployment: Keycloak is reached on the internal network (its backchannel endpoints are internal), while the issuer and the
 * browser-facing endpoints stay public. The discovery document must name the configured issuer.
 */
async function internalDiscovery(config: AuthConfig): Promise<client.Configuration> {
  const response = await fetch(config.discoveryUrl!, { headers: { Accept: "application/json" }, cache: "no-store" });
  if (!response.ok) {
    throw new Error(`OIDC discovery failed with status ${response.status}.`);
  }
  const metadata = (await response.json()) as client.ServerMetadata;
  if (metadata.issuer !== config.issuer) {
    throw new Error("The discovery document names a different issuer.");
  }
  const configuration = new client.Configuration(metadata, config.clientId, config.clientSecret);
  if (config.discoveryUrl!.startsWith("http://")) {
    client.allowInsecureRequests(configuration);
  }
  return configuration;
}

function callbackUrl(kind: OidcClientKind): string {
  return `${configFor(kind).publicUrl}${kind === "platform" ? "/platform/auth/callback" : "/auth/callback"}`;
}

function txStore(kind: OidcClientKind) {
  return kind === "platform" ? getPlatformAuthTxStore() : getAuthTxStore();
}

export interface LoginStart {
  /** Authorization URL to redirect the browser to. */
  url: URL;
  /** Secret for the browser's login binding cookie; the transaction stores only its hash. */
  binding: string;
}

/** Starts the Authorization Code + PKCE flow; the caller sets the binding cookie and redirects to the authorization URL. */
export async function beginLogin(returnTo: string, loginHint?: string, kind: OidcClientKind = "tenant"): Promise<LoginStart> {
  const configuration = await getConfiguration(kind);
  const codeVerifier = client.randomPKCECodeVerifier();
  const codeChallenge = await client.calculatePKCECodeChallenge(codeVerifier);
  const state = client.randomState();
  const nonce = client.randomNonce();

  const binding = newLoginBinding();

  await (await txStore(kind)).set(state, { codeVerifier, nonce, returnTo, bindingHash: binding.hash }, AUTH_TX_TTL_SECONDS);

  const url = client.buildAuthorizationUrl(configuration, {
    redirect_uri: callbackUrl(kind),
    scope: "openid profile email",
    ...(loginHint ? { login_hint: loginHint } : {}),
    code_challenge: codeChallenge,
    code_challenge_method: "S256",
    state,
    nonce,
  });
  return { url, binding: binding.value };
}

export interface CompletedLogin {
  session: SessionData;
  returnTo: string;
}

/**
 * Exchanges the authorization code. `currentUrl` is the callback URL as seen by the browser; `binding` is the browser's login
 * binding cookie, which must belong to the transaction named by `state`.
 */
export async function completeLogin(currentUrl: URL, binding: string | undefined): Promise<CompletedLogin> {
  const { claims, tokens, returnTo } = await exchangeCode(currentUrl, "tenant", binding);
  const { email, name } = identity(claims);
  return {
    returnTo,
    session: {
      sub: claims.sub,
      email,
      name,
      tenantId: typeof claims.tenant_id === "string" ? claims.tenant_id : "",
      accessToken: tokens.access_token,
      accessTokenExpiresAt: Math.floor(Date.now() / 1000) + (tokens.expiresIn() ?? 0),
      refreshToken: tokens.refresh_token,
      idToken: tokens.id_token,
    },
  };
}

export interface CompletedPlatformLogin {
  session: PlatformSessionData;
  returnTo: string;
}

/** Platform callback: the session never carries a tenant id, whatever the token says (ADR-009 §1). */
export async function completePlatformLogin(currentUrl: URL, binding: string | undefined): Promise<CompletedPlatformLogin> {
  const { claims, tokens, returnTo } = await exchangeCode(currentUrl, "platform", binding);
  const { email, name } = identity(claims);
  const now = Math.floor(Date.now() / 1000);
  return {
    returnTo,
    session: {
      kind: "platform",
      sub: claims.sub,
      email,
      name,
      accessToken: tokens.access_token,
      accessTokenExpiresAt: now + (tokens.expiresIn() ?? 0),
      refreshToken: tokens.refresh_token,
      idToken: tokens.id_token,
      createdAt: now,
    },
  };
}

function identity(claims: client.IDToken): { email: string; name: string } {
  const email = typeof claims.email === "string" ? claims.email : "";
  const given = typeof claims.given_name === "string" ? claims.given_name : "";
  const family = typeof claims.family_name === "string" ? claims.family_name : "";
  return { email, name: `${given} ${family}`.trim() || email };
}

async function exchangeCode(currentUrl: URL, kind: OidcClientKind, binding: string | undefined) {
  const state = currentUrl.searchParams.get("state");
  if (state === null) {
    throw new Error("Missing state.");
  }

  const store = await txStore(kind);
  const tx = await store.get(state);
  if (tx === null) {
    throw new Error("Unknown or expired login transaction.");
  }
  await store.delete(state);
  if (!loginBindingMatches(tx.bindingHash, binding)) {
    throw new Error("The login transaction was started in another browser.");
  }

  const configuration = await getConfiguration(kind);
  const tokens = await client.authorizationCodeGrant(configuration, currentUrl, {
    pkceCodeVerifier: tx.codeVerifier,
    expectedState: state,
    expectedNonce: tx.nonce,
    idTokenExpected: true,
  });

  const claims = tokens.claims();
  if (claims === undefined) {
    throw new Error("The token response has no ID token.");
  }

  return { claims, tokens, returnTo: tx.returnTo };
}

/** Returns a session with a fresh access token, or the same session when it is still valid. Null when refresh fails. */
export async function refreshIfNeeded<T extends SessionData | PlatformSessionData>(
  session: T,
  kind: OidcClientKind = "tenant",
): Promise<T | null> {
  const now = Math.floor(Date.now() / 1000);
  if (session.accessTokenExpiresAt - now >= REFRESH_MARGIN_SECONDS) {
    return session;
  }
  if (session.refreshToken === undefined) {
    return null;
  }

  try {
    const tokens = await client.refreshTokenGrant(await getConfiguration(kind), session.refreshToken);
    return {
      ...session,
      accessToken: tokens.access_token,
      accessTokenExpiresAt: now + (tokens.expiresIn() ?? 0),
      refreshToken: tokens.refresh_token ?? session.refreshToken,
      idToken: tokens.id_token ?? session.idToken,
    };
  } catch {
    return null;
  }
}

export async function endSessionUrl(idToken: string | undefined, kind: OidcClientKind = "tenant"): Promise<URL> {
  const config = configFor(kind);
  const parameters: Record<string, string> = {
    post_logout_redirect_uri: kind === "platform" ? `${config.publicUrl}/platform` : `${config.publicUrl}/`,
    client_id: config.clientId,
  };
  if (idToken !== undefined) {
    parameters.id_token_hint = idToken;
  }
  return client.buildEndSessionUrl(await getConfiguration(kind), parameters);
}
