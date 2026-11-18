import type { NextRequest } from "next/server";

/**
 * Fixed-window limit per client for anonymous BFF entry points (SEC-05), the same shape as the API's fixed-window policies.
 * Kept in process memory: the BFF runs as one instance, and an unbounded Redis key per attacker would defeat the purpose.
 */
export class FixedWindowLimiter {
  private readonly windows = new Map<string, { start: number; count: number }>();

  constructor(
    private readonly permitLimit: number,
    private readonly windowMs: number,
    private readonly now: () => number = Date.now,
  ) {}

  /** True when the call is allowed; false once the client used up the current window. */
  tryAcquire(key: string): boolean {
    const now = this.now();
    if (this.windows.size > 10_000) this.prune(now);
    const window = this.windows.get(key);
    if (!window || now - window.start >= this.windowMs) {
      this.windows.set(key, { start: now, count: 1 });
      return true;
    }
    window.count += 1;
    return window.count <= this.permitLimit;
  }

  private prune(now: number) {
    for (const [key, window] of this.windows) {
      if (now - window.start >= this.windowMs) this.windows.delete(key);
    }
  }
}

/**
 * Client address behind the ingress: Cloudflare's `cf-connecting-ip`, else the last `x-forwarded-for` hop (added by the nearest
 * proxy, so a client cannot choose it), else one shared bucket.
 */
export function clientKey(request: NextRequest): string {
  const cloudflare = request.headers.get("cf-connecting-ip")?.trim();
  if (cloudflare) return cloudflare;
  const hops = (request.headers.get("x-forwarded-for") ?? "")
    .split(",")
    .map((hop) => hop.trim())
    .filter(Boolean);
  return hops.at(-1) ?? "unknown";
}

/** `POST /auth/login`: 20 sign-in attempts per client per minute. */
export const loginLimiter = new FixedWindowLimiter(20, 60_000);
