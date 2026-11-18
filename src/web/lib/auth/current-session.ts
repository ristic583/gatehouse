import "server-only";
import { cookies } from "next/headers";
import { getAuthConfig, getPlatformAuthConfig } from "./config";
import { refreshIfNeeded } from "./oidc";
import { readSession, sessionCookieName, type SessionData, updateSession } from "./session";
import { platformSessionCookieName, type PlatformSessionData, platformSessionTtl, readPlatformSession } from "./platform-session";
import { getPlatformSessionStore, getSessionStore } from "./stores";

export interface CurrentSession {
  id: string;
  data: SessionData;
}

/** Loads the session for the current request (cookie → Redis) and refreshes the access token when needed. */
export async function getCurrentSession(): Promise<CurrentSession | null> {
  const jar = await cookies();
  if (!jar.has("gh_session") && !jar.has("__Host-gh_session")) return null;
  const config = getAuthConfig();
  const id = jar.get(sessionCookieName(config.publicUrl))?.value;
  if (id === undefined) {
    return null;
  }

  const store = await getSessionStore();
  const session = await readSession(store, id);
  if (session === null) {
    return null;
  }

  const refreshed = await refreshIfNeeded(session);
  if (refreshed === null) {
    await store.delete(id);
    return null;
  }
  if (refreshed !== session && !(await updateSession(store, id, refreshed))) {
    return null;
  }
  return { id, data: refreshed };
}

export interface CurrentPlatformSession {
  id: string;
  data: PlatformSessionData;
}

/** Loads the platform operator session (`gh_platform_session` → `web:platform-session:{id}`); tenant cookies are ignored. */
export async function getCurrentPlatformSession(): Promise<CurrentPlatformSession | null> {
  const jar = await cookies();
  if (!jar.has("gh_platform_session") && !jar.has("__Host-gh_platform_session")) return null;
  const config = getPlatformAuthConfig();
  const id = jar.get(platformSessionCookieName(config.publicUrl))?.value;
  if (id === undefined) {
    return null;
  }

  const store = await getPlatformSessionStore();
  const session = await readPlatformSession(store, id);
  if (session === null) {
    return null;
  }

  const refreshed = await refreshIfNeeded(session, "platform");
  if (refreshed === null) {
    await store.delete(id);
    return null;
  }
  if (refreshed !== session && !(await store.replace(id, refreshed, Math.max(1, platformSessionTtl(refreshed))))) {
    return null;
  }
  return { id, data: refreshed };
}
