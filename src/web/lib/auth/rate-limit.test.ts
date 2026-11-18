import { NextRequest } from "next/server";
import { describe, expect, it } from "vitest";
import { clientKey, FixedWindowLimiter } from "./rate-limit";

describe("FixedWindowLimiter", () => {
  it("allows the limit per window and per key, then resets", () => {
    let now = 0;
    const limiter = new FixedWindowLimiter(2, 1000, () => now);

    expect([limiter.tryAcquire("a"), limiter.tryAcquire("a"), limiter.tryAcquire("a")]).toEqual([true, true, false]);
    expect(limiter.tryAcquire("b")).toBe(true);
    now = 1000;
    expect(limiter.tryAcquire("a")).toBe(true);
  });
});

describe("clientKey", () => {
  it("prefers cf-connecting-ip, then the last forwarded hop", () => {
    const headers = (h: Record<string, string>) => new NextRequest("https://gatehouse.test/auth/login", { headers: h });

    expect(clientKey(headers({ "cf-connecting-ip": "198.51.100.1", "x-forwarded-for": "1.1.1.1" }))).toBe("198.51.100.1");
    expect(clientKey(headers({ "x-forwarded-for": "6.6.6.6, 203.0.113.7" }))).toBe("203.0.113.7");
    expect(clientKey(headers({}))).toBe("unknown");
  });
});
