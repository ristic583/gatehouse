import { NextRequest } from "next/server";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { SessionData } from "./session";
import { MemorySessionStore } from "./session-store";

/**
 * Security regression (M11, area 3): session cookie flags, same-origin checks on every state-changing BFF route, logout,
 * single-use magic links and the gh_login browser binding, exercised through the route handlers.
 */
const env = vi.hoisted(() => ({ publicUrl: "https://gatehouse.test", store: undefined as unknown }));
vi.mock("server-only", () => ({}));
vi.mock("next/server", async (importOriginal) => ({
  ...(await importOriginal<typeof import("next/server")>()),
  connection: vi.fn().mockResolvedValue(undefined),
}));
vi.mock("@/lib/auth/config", () => ({
  getAuthConfig: () => ({ publicUrl: env.publicUrl, apiBaseUrl: "http://api.internal" }),
  getPlatformAuthConfig: () => ({ publicUrl: env.publicUrl, apiBaseUrl: "http://api.internal" }),
}));
vi.mock("@/lib/auth/stores", () => ({ getSessionStore: async () => env.store }));
vi.mock("@/lib/auth/oidc", () => ({
  beginLogin: vi.fn(),
  completeLogin: vi.fn(),
  endSessionUrl: vi.fn(async () => new URL("https://idp.test/logout")),
}));
vi.mock("@/lib/auth/current-session", () => ({
  getCurrentSession: vi.fn(async () => ({ id: "s", data: { accessToken: "token" } })),
  getCurrentPlatformSession: vi.fn(async () => null),
}));

const { beginLogin, completeLogin } = await import("@/lib/auth/oidc");
const { GET: callback } = await import("@/app/auth/callback/route");
const { POST: logout } = await import("@/app/auth/logout/route");
const { POST: guest } = await import("@/app/auth/guest/route");
const { POST: login } = await import("@/app/auth/login/route");
const proxy = await import("@/app/api/proxy/[...path]/route");

const session: SessionData = {
  sub: "sub-1",
  email: "anna.larsen@nordlys.test",
  name: "Anna Larsen",
  tenantId: "nordlys",
  accessToken: "access",
  accessTokenExpiresAt: Math.floor(Date.now() / 1000) + 300,
  idToken: "id",
};

let store: MemorySessionStore<SessionData>;
const fetchMock = vi.fn<typeof fetch>();

beforeEach(() => {
  env.publicUrl = "https://gatehouse.test";
  store = new MemorySessionStore<SessionData>();
  env.store = store;
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

function sessionCookieLine(response: Response, name: string): string {
  const line = response.headers.getSetCookie().find((l) => l.startsWith(`${name}=`));
  expect(line).toBeDefined();
  return line ?? "";
}

function guestForm(token: string, origin = "https://gatehouse.test") {
  return new NextRequest("https://gatehouse.test/auth/guest", {
    method: "POST",
    headers: { origin, "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ token }),
  });
}

describe("session cookie", () => {
  it("is httpOnly, SameSite=Lax, Secure and __Host- on https", async () => {
    vi.mocked(completeLogin).mockResolvedValue({ session, returnTo: "/map" });

    const response = await callback(
      new NextRequest("https://gatehouse.test/auth/callback?code=c&state=s", { headers: { cookie: "__Host-gh_login=b" } }),
    );

    const line = sessionCookieLine(response, "__Host-gh_session");
    expect(line).toMatch(/HttpOnly/i);
    expect(line).toMatch(/SameSite=Lax/i);
    expect(line).toMatch(/Secure/i);
    expect(line).toMatch(/Path=\//);
  });

  it("is httpOnly and SameSite but not Secure on local http", async () => {
    env.publicUrl = "http://localhost:3000";
    vi.mocked(completeLogin).mockResolvedValue({ session, returnTo: "/map" });

    const response = await callback(
      new NextRequest("http://localhost:3000/auth/callback?code=c&state=s", { headers: { cookie: "gh_login=b" } }),
    );

    const line = sessionCookieLine(response, "gh_session");
    expect(line).toMatch(/HttpOnly/i);
    expect(line).toMatch(/SameSite=Lax/i);
    expect(line).not.toMatch(/Secure/i);
  });
});

describe("same-origin check on state-changing requests", () => {
  it.each([undefined, "null", "https://evil.test", "http://gatehouse.test", "https://gatehouse.test.evil.test"])(
    "refuses origin %s on logout, guest, sign-in and the API proxy",
    async (origin) => {
      const headers: Record<string, string> = origin === undefined ? {} : { origin };
      const id = "kept-session";
      await store.set(id, session, 60);
      const cookie = `__Host-gh_session=${id}`;

      const responses = [
        await logout(new NextRequest("https://gatehouse.test/auth/logout", { method: "POST", headers: { ...headers, cookie } })),
        await guest(
          new NextRequest("https://gatehouse.test/auth/guest", { method: "POST", headers, body: new URLSearchParams({ token: "t" }) }),
        ),
        await login(
          new NextRequest("https://gatehouse.test/auth/login", {
            method: "POST",
            headers,
            body: new URLSearchParams({ email: "a@b.test" }),
          }),
        ),
        ...(await Promise.all(
          (["POST", "PUT", "PATCH", "DELETE"] as const).map((method) =>
            proxy[method](
              new NextRequest("https://gatehouse.test/api/proxy/v1/access-requests", { method, headers: { ...headers, cookie } }),
              {
                params: Promise.resolve({ path: ["v1", "access-requests"] }),
              },
            ),
          ),
        )),
      ];

      expect(responses.map((r) => r.status)).toEqual(Array(responses.length).fill(403));
      expect(fetchMock).not.toHaveBeenCalled();
      expect(beginLogin).not.toHaveBeenCalled();
      expect(await store.get(id)).not.toBeNull();
    },
  );

  it("forwards a same-origin proxy write with the bearer token only", async () => {
    fetchMock.mockResolvedValue(new Response("{}", { status: 201, headers: { "content-type": "application/json" } }));
    const request = new NextRequest("https://gatehouse.test/api/proxy/v1/access-requests", {
      method: "POST",
      headers: {
        origin: "https://gatehouse.test",
        cookie: "__Host-gh_session=x",
        "x-test-tenant": "bricks",
        "content-type": "application/json",
      },
      body: "{}",
    });

    const response = await proxy.POST(request, { params: Promise.resolve({ path: ["v1", "access-requests"] }) });

    expect(response.status).toBe(201);
    const headers = new Headers(fetchMock.mock.calls[0]?.[1]?.headers);
    expect(headers.get("authorization")).toBe("Bearer token");
    expect(headers.get("cookie")).toBeNull();
    expect(headers.get("x-test-tenant")).toBeNull();
  });
});

describe("logout", () => {
  it("deletes the server session and expires the cookie", async () => {
    const id = "session-to-end";
    await store.set(id, session, 60);

    const response = await logout(
      new NextRequest("https://gatehouse.test/auth/logout", {
        method: "POST",
        headers: { origin: "https://gatehouse.test", cookie: `__Host-gh_session=${id}` },
      }),
    );

    expect(await store.get(id)).toBeNull();
    const line = sessionCookieLine(response, "__Host-gh_session");
    expect(line).toMatch(/^__Host-gh_session=;/);
    expect(line).toMatch(/Max-Age=0|Expires=Thu, 01 Jan 1970/i);
    expect(line).toMatch(/Secure/i);
  });
});

describe("magic link", () => {
  it("starts a guest session once; the second redemption gets no session", async () => {
    const dto = {
      accessToken: "guest-token",
      expiresAt: new Date(Date.now() + 30 * 60_000).toISOString(),
      sub: "g",
      email: "g@x.test",
      name: "G",
      tenantId: "nordlys",
    };
    fetchMock
      .mockResolvedValueOnce(new Response(JSON.stringify(dto), { status: 200, headers: { "content-type": "application/json" } }))
      .mockResolvedValueOnce(new Response("{}", { status: 401 }));

    const first = await guest(guestForm("one-time"));
    const second = await guest(guestForm("one-time"));

    expect(first.headers.get("location")).toBe("https://gatehouse.test/map");
    expect(sessionCookieLine(first, "__Host-gh_session")).toMatch(/HttpOnly/i);
    expect(second.headers.get("location")).toBe("https://gatehouse.test/guest?error=invalid");
    expect(second.headers.getSetCookie().some((l) => l.startsWith("__Host-gh_session="))).toBe(false);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(String(fetchMock.mock.calls[0]?.[0])).toBe("http://api.internal/api/v1/guest/sessions");
  });
});

describe("sign-in binding (gh_login)", () => {
  it("hands the browser's binding cookie to the code exchange and creates no session when it is refused", async () => {
    vi.mocked(completeLogin).mockRejectedValue(new Error("The login transaction was started in another browser."));

    const response = await callback(
      new NextRequest("https://gatehouse.test/auth/callback?code=c&state=s", { headers: { cookie: "__Host-gh_login=attacker" } }),
    );

    expect(completeLogin).toHaveBeenCalledWith(expect.any(URL), "attacker");
    expect(response.status).toBe(400);
    expect(response.headers.getSetCookie().some((l) => l.startsWith("__Host-gh_session="))).toBe(false);
    expect(sessionCookieLine(response, "__Host-gh_login")).toMatch(/^__Host-gh_login=;/);
  });

  it("passes no binding when the cookie is missing", async () => {
    vi.mocked(completeLogin).mockRejectedValue(new Error("binding"));

    const response = await callback(new NextRequest("https://gatehouse.test/auth/callback?code=c&state=s"));

    expect(completeLogin).toHaveBeenCalledWith(expect.any(URL), undefined);
    expect(response.status).toBe(400);
  });
});

describe("rate limits", () => {
  it("SEC-05: the sign-in form POST /auth/login is rate limited per client", async () => {
    vi.mocked(beginLogin).mockResolvedValue({ url: new URL("https://idp.test/authorize"), binding: "b" });
    const statuses: number[] = [];
    for (let i = 0; i < 30; i++) {
      const request = new NextRequest("https://gatehouse.test/auth/login", {
        method: "POST",
        headers: { origin: "https://gatehouse.test", "x-forwarded-for": "203.0.113.7" },
        body: new URLSearchParams({ email: "a@b.test" }),
      });
      statuses.push((await login(request)).status);
    }
    expect(statuses).toContain(429);
  });
});
