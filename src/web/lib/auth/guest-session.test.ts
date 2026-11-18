import { afterEach, describe, expect, it, vi } from "vitest";
import { MemorySessionStore } from "./session-store";
import { createSession, GUEST_SESSION_TTL_SECONDS, readSession, SESSION_TTL_SECONDS, sessionTtl, type SessionData } from "./session";

vi.mock("server-only", () => ({}));
vi.mock("./config", () => ({
  getAuthConfig: () => ({
    issuer: "http://kc",
    clientId: "web",
    clientSecret: "s",
    publicUrl: "http://localhost:3000",
    apiBaseUrl: "http://api",
  }),
}));
vi.mock("./stores", () => ({ getAuthTxStore: vi.fn() }));
const refreshTokenGrant = vi.fn();
vi.mock("openid-client", () => ({ refreshTokenGrant, discovery: vi.fn(), allowInsecureRequests: vi.fn() }));

function guest(expiresAt: number): SessionData {
  return {
    sub: "guest:p-1",
    email: "gus@x.test",
    name: "Gus Guest",
    tenantId: "nordlys",
    accessToken: "jwt",
    accessTokenExpiresAt: expiresAt,
    guest: true,
  };
}

afterEach(() => {
  vi.useRealTimers();
  refreshTokenGrant.mockReset();
});

describe("guest session", () => {
  it("lives at most 30 minutes and never slides past its token", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-10T12:00:00Z"));
    const store = new MemorySessionStore<SessionData>(() => Date.now());
    const now = Math.floor(Date.now() / 1000);
    const data = guest(now + GUEST_SESSION_TTL_SECONDS);

    expect(GUEST_SESSION_TTL_SECONDS).toBe(1800);
    expect(sessionTtl(data, now)).toBe(1800);
    expect(sessionTtl(guest(now + 7200), now)).toBe(1800);
    expect(sessionTtl({ ...data, guest: undefined, refreshToken: "r" }, now)).toBe(SESSION_TTL_SECONDS);

    const id = await createSession(store, data);
    vi.setSystemTime(Date.now() + 20 * 60 * 1000);
    expect(await readSession(store, id)).not.toBeNull();
    vi.setSystemTime(Date.now() + 11 * 60 * 1000);
    expect(await readSession(store, id)).toBeNull();
  });

  it("is not refreshed after expiry", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-10T12:00:00Z"));
    const { refreshIfNeeded } = await import("./oidc");
    const now = Math.floor(Date.now() / 1000);

    expect(await refreshIfNeeded(guest(now + 1200))).not.toBeNull();
    expect(await refreshIfNeeded(guest(now - 1))).toBeNull();
    expect(refreshTokenGrant).not.toHaveBeenCalled();
  });
});
