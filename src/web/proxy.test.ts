import { afterEach, describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";
import { proxy } from "./proxy";
afterEach(() => vi.unstubAllEnvs());
describe("email entry and preview routing", () => {
  it("routes unauthenticated workspace requests to the branded login", () => {
    const r = proxy(new NextRequest("https://app.test/approvals?tab=pending"));
    expect(r.headers.get("location")).toBe("https://app.test/login?returnTo=%2Fapprovals%3Ftab%3Dpending");
  });
  it("makes the email screen public without creating a session", () => {
    const r = proxy(new NextRequest("https://app.test/login"));
    expect(r.headers.get("x-middleware-next")).toBe("1");
    expect(r.headers.get("set-cookie")).toBeNull();
  });
  it("does not make the design preview public in production", () => {
    vi.stubEnv("NODE_ENV", "production");
    expect(proxy(new NextRequest("https://app.test/design-preview")).headers.get("location")).toContain("/login?");
  });
  it("allows the isolated preview only in development", () => {
    vi.stubEnv("NODE_ENV", "development");
    expect(proxy(new NextRequest("https://app.test/design-preview")).headers.get("x-middleware-next")).toBe("1");
  });
});
