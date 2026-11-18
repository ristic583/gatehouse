import { NextRequest } from "next/server";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  createPlatformSession,
  PLATFORM_SESSION_IDLE_SECONDS,
  PLATFORM_SESSION_MAX_SECONDS,
  platformSessionCookieName,
  type PlatformSessionData,
  proxySessionFor,
  readPlatformSession,
  safePlatformReturnTo,
} from "./platform-session";
import type { SessionData } from "./session";
import { MemorySessionStore } from "./session-store";

const PUBLIC_URL = "https://gatehouse.test";
const stores = vi.hoisted(() => ({ tenant: undefined as unknown, platform: undefined as unknown }));
vi.mock("server-only", () => ({}));
vi.mock("next/server", async (importOriginal) => ({
  ...(await importOriginal<typeof import("next/server")>()),
  connection: vi.fn().mockResolvedValue(undefined),
}));
vi.mock("@/lib/auth/config", () => ({
  getAuthConfig: () => ({ publicUrl: "https://gatehouse.test" }),
  getPlatformAuthConfig: () => ({ publicUrl: "https://gatehouse.test" }),
}));
vi.mock("@/lib/auth/stores", () => ({
  getSessionStore: async () => stores.tenant,
  getPlatformSessionStore: async () => stores.platform,
}));
vi.mock("@/lib/auth/oidc", () => ({
  completeLogin: vi.fn(),
  endSessionUrl: vi.fn(async () => new URL("https://idp.test/logout")),
}));
const { completeLogin } = await import("@/lib/auth/oidc");
const { GET: tenantCallback } = await import("@/app/auth/callback/route");
const { POST: tenantLogout } = await import("@/app/auth/logout/route");

const start = 1_800_000_000;
const data: PlatformSessionData = {
  kind: "platform",
  sub: "op-1",
  email: "operator@platform.test",
  name: "Platform Operator",
  accessToken: "access",
  accessTokenExpiresAt: start + 300,
  createdAt: start,
};

describe("platform session", () => {
  it("platform session cannot call tenant proxy paths", () => {
    const platformOnly = { tenant: false, platform: true };
    expect(proxySessionFor(["v1", "me"], platformOnly)).toBeNull();
    expect(proxySessionFor(["v1", "admin", "sites"], platformOnly)).toBeNull();
    expect(proxySessionFor(["v1", "platform", "tenants"], platformOnly)).toBe("platform");
    expect(proxySessionFor(["v1", "platform", "..", "me"], platformOnly)).toBeNull();
    // With both cookies a tenant path still uses only the tenant session.
    expect(proxySessionFor(["v1", "me"], { tenant: true, platform: true })).toBe("tenant");
  });

  it("tenant session cannot call platform proxy paths", () => {
    const tenantOnly = { tenant: true, platform: false };
    expect(proxySessionFor(["v1", "platform", "tenants"], tenantOnly)).toBeNull();
    expect(proxySessionFor(["V1", "Platform", "alerts"], tenantOnly)).toBeNull();
    expect(proxySessionFor(["v1", "me"], tenantOnly)).toBe("tenant");
    expect(proxySessionFor(["v1", "platform", "tenants"], { tenant: true, platform: true })).toBe("platform");
  });

  it("expires after 30 minutes idle and 8 hours absolute", async () => {
    let nowMs = start * 1000;
    const store = new MemorySessionStore<PlatformSessionData>(() => nowMs);
    const id = await createPlatformSession(store, data);

    nowMs += (PLATFORM_SESSION_IDLE_SECONDS - 10) * 1000;
    expect(await readPlatformSession(store, id, Math.floor(nowMs / 1000))).toEqual(data);

    nowMs = (start + PLATFORM_SESSION_MAX_SECONDS + 1) * 1000;
    expect(await readPlatformSession(store, id, Math.floor(nowMs / 1000))).toBeNull();
  });

  it("uses its own cookie name and keeps return targets inside /platform", () => {
    expect(platformSessionCookieName("https://gatehouse.example.com")).toBe("__Host-gh_platform_session");
    expect(platformSessionCookieName("http://localhost:3000")).toBe("gh_platform_session");
    expect(safePlatformReturnTo("/platform/tenants/acme")).toBe("/platform/tenants/acme");
    expect(safePlatformReturnTo("/map")).toBe("/platform");
    expect(safePlatformReturnTo("//evil.test/platform")).toBe("/platform");
  });
});

const tenantSession: SessionData = {
  sub: "u-1",
  email: "anna@nordlys.test",
  name: "Anna",
  tenantId: "nordlys",
  accessToken: "tenant-access",
  accessTokenExpiresAt: start + 300,
};

/** Every Set-Cookie line of a response, so the test sees exactly which cookies a route writes or expires. */
function setCookies(response: Response): string[] {
  return response.headers.getSetCookie();
}

describe("platform and tenant sessions side by side", () => {
  let platformStore: MemorySessionStore<PlatformSessionData>;
  let tenantStore: MemorySessionStore<SessionData>;
  let platformId: string;

  beforeEach(async () => {
    platformStore = new MemorySessionStore<PlatformSessionData>();
    tenantStore = new MemorySessionStore<SessionData>();
    stores.platform = platformStore;
    stores.tenant = tenantStore;
    platformId = await createPlatformSession(platformStore, { ...data, createdAt: Math.floor(Date.now() / 1000) });
  });

  it("tenant sign-in does not clear the platform session", async () => {
    vi.mocked(completeLogin).mockResolvedValue({ session: tenantSession, returnTo: "/map" });
    const request = new NextRequest(`${PUBLIC_URL}/auth/callback?code=c&state=s`, {
      headers: { cookie: `__Host-gh_platform_session=${platformId}; __Host-gh_platform_login=p; __Host-gh_login=t` },
    });

    const response = await tenantCallback(request);

    const written = setCookies(response);
    expect(written.some((line) => line.startsWith("__Host-gh_session="))).toBe(true);
    expect(written.filter((line) => line.includes("gh_platform"))).toEqual([]);
    // The tenant binding cookie is expired with Secure + Path=/ so the browser actually drops the __Host- cookie.
    expect(written.find((line) => line.startsWith("__Host-gh_login="))).toMatch(/Path=\/.*Secure|Secure.*Path=\//);
    expect(await platformStore.get(platformId)).not.toBeNull();
  });

  it("tenant logout keeps the platform session", async () => {
    const tenantId = "tenant-1";
    await tenantStore.set(tenantId, tenantSession, 60);
    const request = new NextRequest(`${PUBLIC_URL}/auth/logout`, {
      method: "POST",
      headers: { origin: PUBLIC_URL, cookie: `__Host-gh_session=${tenantId}; __Host-gh_platform_session=${platformId}` },
    });

    const response = await tenantLogout(request);

    const written = setCookies(response);
    expect(written).toHaveLength(1);
    expect(written[0]).toMatch(/^__Host-gh_session=;/);
    expect(written[0]).toContain("Secure");
    expect(await tenantStore.get(tenantId)).toBeNull();
    expect(await platformStore.get(platformId)).not.toBeNull();
  });
});
