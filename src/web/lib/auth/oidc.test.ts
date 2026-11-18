import { afterEach, describe, expect, it, vi } from "vitest";
import { MemorySessionStore } from "./session-store";
import type { AuthTransaction } from "./stores";

vi.mock("server-only", () => ({}));
const config = {
  issuer: "https://kc.test/realms/gatehouse",
  clientId: "web",
  clientSecret: "s",
  publicUrl: "https://gatehouse.test",
  apiBaseUrl: "http://api",
};
vi.mock("./config", () => ({ getAuthConfig: () => config, getPlatformAuthConfig: () => config }));
const txStore = new MemorySessionStore<AuthTransaction>();
vi.mock("./stores", () => ({ getAuthTxStore: () => Promise.resolve(txStore), getPlatformAuthTxStore: () => Promise.resolve(txStore) }));
const authorizationCodeGrant = vi.fn();
let state = 0;
vi.mock("openid-client", () => ({
  discovery: vi.fn().mockResolvedValue({}),
  allowInsecureRequests: vi.fn(),
  randomPKCECodeVerifier: () => "verifier",
  calculatePKCECodeChallenge: () => Promise.resolve("challenge"),
  randomState: () => `state-${++state}`,
  randomNonce: () => "nonce",
  buildAuthorizationUrl: (_: unknown, parameters: Record<string, string>) => new URL(`https://kc.test/auth?state=${parameters.state}`),
  authorizationCodeGrant,
}));

const { beginLogin, completeLogin } = await import("./oidc");

function callback(authorizationUrl: URL): URL {
  return new URL(`https://gatehouse.test/auth/callback?code=c&state=${authorizationUrl.searchParams.get("state")}`);
}

afterEach(() => authorizationCodeGrant.mockReset());

describe("OIDC login transaction (GH-AUTH-01)", () => {
  it("completes only in the browser that started the login", async () => {
    authorizationCodeGrant.mockResolvedValue({
      access_token: "at",
      refresh_token: "rt",
      id_token: "it",
      expiresIn: () => 300,
      claims: () => ({ sub: "sub-1", email: "anna@nordlys.test", given_name: "Anna", family_name: "Larsen", tenant_id: "nordlys" }),
    });
    const start = await beginLogin("/map");

    const completed = await completeLogin(callback(start.url), start.binding);

    expect(completed.session.sub).toBe("sub-1");
    expect(completed.returnTo).toBe("/map");
  });

  it("refuses a callback opened in another browser and burns the transaction", async () => {
    const attacker = await beginLogin("/map");

    await expect(completeLogin(callback(attacker.url), undefined)).rejects.toThrow();
    await expect(completeLogin(callback(attacker.url), attacker.binding)).rejects.toThrow("Unknown or expired login transaction.");
    expect(authorizationCodeGrant).not.toHaveBeenCalled();
  });

  it("refuses a binding of a different login", async () => {
    const first = await beginLogin("/map");
    const second = await beginLogin("/map");

    await expect(completeLogin(callback(first.url), second.binding)).rejects.toThrow("another browser");
    expect(authorizationCodeGrant).not.toHaveBeenCalled();
  });
});
