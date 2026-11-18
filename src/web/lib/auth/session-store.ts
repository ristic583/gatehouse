import type { Redis } from "ioredis";

export interface SessionStore<T> {
  get(id: string): Promise<T | null>;
  set(id: string, data: T, ttlSeconds: number): Promise<void>;
  /**
   * Writes only while the entry still exists (atomic in Redis: `SET ... XX`); false when it is gone. Sliding and refresh writes
   * use it so a request that read the session before a logout cannot recreate it afterwards (GH-AUTH-04).
   */
  replace(id: string, data: T, ttlSeconds: number): Promise<boolean>;
  delete(id: string): Promise<void>;
}

export const SESSION_PREFIX = "web:session:";
export const AUTH_TX_PREFIX = "web:authtx:";

/** Redis-backed store; `prefix` is `web:session:` for sessions or `web:authtx:` for login transactions. */
export class RedisSessionStore<T> implements SessionStore<T> {
  constructor(
    private readonly redis: Redis,
    private readonly prefix: string,
  ) {}

  async get(id: string): Promise<T | null> {
    const raw = await this.redis.get(this.prefix + id);
    return raw === null ? null : (JSON.parse(raw) as T);
  }

  async set(id: string, data: T, ttlSeconds: number): Promise<void> {
    await this.redis.set(this.prefix + id, JSON.stringify(data), "EX", ttlSeconds);
  }

  async replace(id: string, data: T, ttlSeconds: number): Promise<boolean> {
    return (await this.redis.set(this.prefix + id, JSON.stringify(data), "EX", ttlSeconds, "XX")) === "OK";
  }

  async delete(id: string): Promise<void> {
    await this.redis.del(this.prefix + id);
  }
}

/** In-memory store for tests. `now` returns milliseconds. */
export class MemorySessionStore<T> implements SessionStore<T> {
  private readonly entries = new Map<string, { data: T; expiresAt: number }>();

  constructor(private readonly now: () => number = Date.now) {}

  get(id: string): Promise<T | null> {
    const entry = this.entries.get(id);
    if (entry === undefined) {
      return Promise.resolve(null);
    }
    if (entry.expiresAt <= this.now()) {
      this.entries.delete(id);
      return Promise.resolve(null);
    }
    return Promise.resolve(entry.data);
  }

  set(id: string, data: T, ttlSeconds: number): Promise<void> {
    this.entries.set(id, { data, expiresAt: this.now() + ttlSeconds * 1000 });
    return Promise.resolve();
  }

  async replace(id: string, data: T, ttlSeconds: number): Promise<boolean> {
    if ((await this.get(id)) === null) {
      return false;
    }
    await this.set(id, data, ttlSeconds);
    return true;
  }

  delete(id: string): Promise<void> {
    this.entries.delete(id);
    return Promise.resolve();
  }
}
