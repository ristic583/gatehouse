import { describe, expect, it } from "vitest";
import nextConfig from "./next.config";

/** Security regression (M11, area 5): the web app sends browser security headers on every page. */
describe("security headers", () => {
  it("SEC-08: next.config sets CSP, X-Frame-Options, X-Content-Type-Options and HSTS headers", async () => {
    const rules = (await nextConfig.headers?.()) ?? [];
    const all = rules.filter((r) => r.source === "/:path*").flatMap((r) => r.headers);
    const value = (key: string) => all.find((h) => h.key.toLowerCase() === key)?.value ?? "";

    expect(value("content-security-policy")).toContain("frame-ancestors 'none'");
    expect(value("x-frame-options")).toBe("DENY");
    expect(value("x-content-type-options")).toBe("nosniff");
    expect(value("strict-transport-security")).toContain("max-age=");
  });
});
