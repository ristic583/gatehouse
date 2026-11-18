import { afterEach, describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";
vi.mock("@/lib/auth/oidc", () => ({ beginLogin: vi.fn() }));
vi.mock("next/server", async (importOriginal) => ({
  ...(await importOriginal<typeof import("next/server")>()),
  connection: vi.fn().mockResolvedValue(undefined),
}));
import { beginLogin } from "@/lib/auth/oidc";
import { GET, POST } from "./route";
const login = vi.mocked(beginLogin);
function post(email: string, origin = "https://gatehouse.test", returnTo = "/map") {
  return new NextRequest("https://gatehouse.test/auth/login", {
    method: "POST",
    headers: { origin, "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ email, returnTo }),
  });
}
afterEach(() => vi.clearAllMocks());
describe("email-first sign-in", () => {
  it("shows the app screen before the identity provider", async () => {
    const r = await GET(new NextRequest("https://gatehouse.test/auth/login?returnTo=/approvals"));
    expect(r.headers.get("location")).toBe("https://gatehouse.test/login?returnTo=%2Fapprovals");
    expect(login).not.toHaveBeenCalled();
  });
  it("passes email as a hint while preserving a safe return target", async () => {
    login.mockResolvedValue({ url: new URL("https://idp.test/authorize"), binding: "browser-secret" });
    const r = await POST(post(" SOFIE@example.test ", undefined, "/requests"));
    expect(login).toHaveBeenCalledWith("/requests", "sofie@example.test");
    expect(r.status).toBe(303);
    expect(r.headers.get("location")).toBe("https://idp.test/authorize");
    expect(r.cookies.get("__Host-gh_login")?.value).toBe("browser-secret");
    expect(r.cookies.get("__Host-gh_login")?.httpOnly).toBe(true);
  });
  it("rejects a cross-origin sign-in submission", async () => {
    const r = await POST(post("a@example.test", "https://other.test"));
    expect(r.status).toBe(403);
    expect(login).not.toHaveBeenCalled();
  });
  it("validates email on the server", async () => {
    const r = await POST(post("invalid"));
    expect(r.headers.get("location")).toContain("error=email");
    expect(login).not.toHaveBeenCalled();
  });
  it("does not accept an external return URL", async () => {
    login.mockResolvedValue({ url: new URL("https://idp.test/authorize"), binding: "b" });
    await POST(post("a@example.test", undefined, "https://other.test"));
    expect(login).toHaveBeenCalledWith("/", "a@example.test");
  });
  it("provides a retryable error without exposing provider details", async () => {
    login.mockRejectedValue(new Error("private provider diagnostic"));
    const r = await POST(post("a@example.test"));
    expect(r.headers.get("location")).toContain("error=unavailable");
    expect(r.headers.get("location")).not.toContain("private");
  });
});
