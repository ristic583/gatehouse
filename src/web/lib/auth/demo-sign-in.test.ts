import { NextRequest } from "next/server";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemorySessionStore } from "./session-store";

const env = vi.hoisted(() => ({ store: undefined as unknown }));
vi.mock("server-only", () => ({}));
vi.mock("next/server", async (importOriginal) => ({
  ...(await importOriginal<typeof import("next/server")>()),
  connection: vi.fn().mockResolvedValue(undefined),
}));
vi.mock("@/lib/auth/config", () => ({ getAuthConfig: () => ({ publicUrl: "https://demo.test", apiBaseUrl: "http://api.internal" }) }));
vi.mock("@/lib/auth/stores", () => ({ getSessionStore: async () => env.store }));
vi.mock("./oidc", () => ({ beginLogin: vi.fn(), completeLogin: vi.fn() }));
vi.mock("@/lib/auth/oidc", () => ({ beginLogin: vi.fn(), completeLogin: vi.fn() }));

const oidc = await import("./oidc");
const { CookieJar, parseForm, signInAsDemoPersona } = await import("./demo-sign-in");
const { POST: demoRoute } = await import("@/app/auth/demo/route");

function html(body: string, init: ResponseInit = {}): Response {
  return new Response(body, { status: 200, headers: { "content-type": "text/html" }, ...init });
}
function redirect(location: string, cookies: string[] = []): Response {
  const headers = new Headers({ location });
  for (const c of cookies) headers.append("set-cookie", c);
  return new Response(null, { status: 302, headers });
}

describe("CookieJar", () => {
  it("Header_twoOriginsWithSameCookieName_keepsThemApart", () => {
    const jar = new CookieJar();
    jar.store(new URL("https://kc.test/realms/gatehouse/x"), ["AUTH_SESSION_ID=a; Path=/realms/gatehouse/; HttpOnly"]);
    jar.store(new URL("https://idp.test/realms/nordlys-oidc/x"), ["AUTH_SESSION_ID=b; Path=/realms/nordlys-oidc/"]);
    expect(jar.header(new URL("https://kc.test/realms/gatehouse/login"))).toBe("AUTH_SESSION_ID=a");
    expect(jar.header(new URL("https://idp.test/realms/nordlys-oidc/login"))).toBe("AUTH_SESSION_ID=b");
    expect(jar.header(new URL("https://kc.test/realms/other/login"))).toBe("");
  });

  it("Store_expiredCookie_removesIt", () => {
    const jar = new CookieJar();
    const url = new URL("https://kc.test/");
    jar.store(url, ["KC_RESTART=x; Path=/"]);
    jar.store(url, ["KC_RESTART=; Max-Age=0; Path=/"]);
    expect(jar.header(url)).toBe("");
  });
});

describe("parseForm", () => {
  it("ParseForm_keycloakLoginPage_returnsActionAndFields", () => {
    const page = `<form id="kc-form-login" action="https://idp.test/realms/r/login-actions/authenticate?session_code=a&amp;tab_id=b" method="post">
      <input id="username" name="username" value="" type="text"><input name="password" type="password">
      <input type="hidden" id="id-hidden-input" name="credentialId"></form>`;
    const form = parseForm(page, new URL("https://idp.test/realms/r/protocol/openid-connect/auth"));
    expect(form?.action.href).toBe("https://idp.test/realms/r/login-actions/authenticate?session_code=a&tab_id=b");
    expect(Object.keys(form?.fields ?? {})).toEqual(["username", "password", "credentialId"]);
  });
});

describe("signInAsDemoPersona", () => {
  beforeEach(() => {
    vi.mocked(oidc.beginLogin).mockResolvedValue({ url: new URL("https://kc.test/realms/gatehouse/auth?login_hint=x"), binding: "bind" });
    vi.mocked(oidc.completeLogin).mockResolvedValue({
      returnTo: "/map",
      session: { sub: "s", email: "e", name: "n", tenantId: "nordlys", accessToken: "t", accessTokenExpiresAt: 1 },
    });
  });

  it("SignIn_brokeredOidcFlow_postsPasswordOnlyToUpstreamAndCompletesAtCallback", async () => {
    const posts: { url: string; body: string }[] = [];
    const fetcher = vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "POST") posts.push({ url, body: String(init.body) });
      if (url.startsWith("https://kc.test/realms/gatehouse/auth"))
        return redirect("https://idp.test/realms/nordlys-oidc/auth", ["KC_RESTART=1; Path=/realms/gatehouse/"]);
      if (url === "https://idp.test/realms/nordlys-oidc/auth")
        return html(`<form action="/realms/nordlys-oidc/login-actions/authenticate"><input name="username"><input name="password"></form>`);
      if (url.endsWith("/login-actions/authenticate")) return redirect("https://kc.test/realms/gatehouse/broker/nordlys/endpoint?code=c");
      if (url.startsWith("https://kc.test/realms/gatehouse/broker/")) return redirect("https://demo.test/auth/callback?code=final&state=s");
      throw new Error(`unexpected ${url}`);
    });

    const result = await signInAsDemoPersona("anna.larsen@nordlys.test", "/map", "https://demo.test/auth/callback", "pw", fetcher);

    expect(result.returnTo).toBe("/map");
    expect(posts).toEqual([
      { url: "https://idp.test/realms/nordlys-oidc/login-actions/authenticate", body: "username=anna.larsen&password=pw" },
    ]);
    expect(vi.mocked(oidc.completeLogin)).toHaveBeenCalledWith(new URL("https://demo.test/auth/callback?code=final&state=s"), "bind");
  });

  it("SignIn_unknownPersona_throwsBeforeAnyRequest", async () => {
    const fetcher = vi.fn();
    await expect(signInAsDemoPersona("someone@else.test", "/", "https://demo.test/auth/callback", "pw", fetcher)).rejects.toThrow();
    expect(fetcher).not.toHaveBeenCalled();
  });
});

describe("POST /auth/demo", () => {
  const request = (body: Record<string, string>, origin = "https://demo.test") =>
    new NextRequest("https://demo.test/auth/demo", { method: "POST", headers: { origin }, body: new URLSearchParams(body) });

  beforeEach(() => {
    env.store = new MemorySessionStore();
  });
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("Demo_disabled_returns404", async () => {
    vi.stubEnv("GATEHOUSE_DEMO_SIGN_IN", "");
    expect((await demoRoute(request({ persona: "anna.larsen@nordlys.test" }))).status).toBe(404);
  });

  it("Demo_crossOrigin_returns403", async () => {
    vi.stubEnv("GATEHOUSE_DEMO_SIGN_IN", "true");
    vi.stubEnv("GATEHOUSE_DEMO_PASSWORD", "pw");
    expect((await demoRoute(request({ persona: "anna.larsen@nordlys.test" }, "https://evil.test"))).status).toBe(403);
  });

  it("Demo_unknownPersona_redirectsToLoginWithError", async () => {
    vi.stubEnv("GATEHOUSE_DEMO_SIGN_IN", "true");
    vi.stubEnv("GATEHOUSE_DEMO_PASSWORD", "pw");
    const response = await demoRoute(request({ persona: "operator@platform.test" }));
    expect(response.status).toBe(303);
    expect(response.headers.get("location")).toBe("https://demo.test/login?error=persona");
  });
});
