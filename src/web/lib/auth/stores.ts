import "server-only";
import { PLATFORM_AUTH_TX_PREFIX, PLATFORM_SESSION_PREFIX, type PlatformSessionData } from "./platform-session";
import { getRedis } from "./redis";
import type { SessionData } from "./session";
import { AUTH_TX_PREFIX, RedisSessionStore, SESSION_PREFIX } from "./session-store";

export interface AuthTransaction {
  codeVerifier: string;
  nonce: string;
  returnTo: string;
  /** SHA-256 of the browser binding cookie (GH-AUTH-01). */
  bindingHash?: string;
}

export async function getSessionStore(): Promise<RedisSessionStore<SessionData>> {
  return new RedisSessionStore<SessionData>(await getRedis(), SESSION_PREFIX);
}

export async function getAuthTxStore(): Promise<RedisSessionStore<AuthTransaction>> {
  return new RedisSessionStore<AuthTransaction>(await getRedis(), AUTH_TX_PREFIX);
}

export async function getPlatformSessionStore(): Promise<RedisSessionStore<PlatformSessionData>> {
  return new RedisSessionStore<PlatformSessionData>(await getRedis(), PLATFORM_SESSION_PREFIX);
}

export async function getPlatformAuthTxStore(): Promise<RedisSessionStore<AuthTransaction>> {
  return new RedisSessionStore<AuthTransaction>(await getRedis(), PLATFORM_AUTH_TX_PREFIX);
}
