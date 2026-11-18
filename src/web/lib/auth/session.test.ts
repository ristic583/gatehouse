import { describe, expect, it } from "vitest";
import { createSession, readSession, SESSION_TTL_SECONDS, sessionCookieName, type SessionData, updateSession } from "./session";
import { MemorySessionStore } from "./session-store";

const data: SessionData = {
  sub: "sub-1",
  email: "anna.larsen@nordlys.test",
  name: "Anna Larsen",
  tenantId: "nordlys",
  accessToken: "access",
  accessTokenExpiresAt: 0,
  refreshToken: "refresh",
  idToken: "id",
};

describe("session", () => {
  it("creates and reads a session", async () => {
    const store = new MemorySessionStore<SessionData>();
    const id = await createSession(store, data);

    expect(Buffer.from(id, "base64url")).toHaveLength(32);
    expect(await readSession(store, id)).toEqual(data);
  });

  it("expired session returns null", async () => {
    let now = 1_000_000;
    const store = new MemorySessionStore<SessionData>(() => now);
    const id = await createSession(store, data);

    now += (SESSION_TTL_SECONDS + 1) * 1000;

    expect(await readSession(store, id)).toBeNull();
  });

  it("a refresh that read the session before logout cannot recreate it (GH-AUTH-04)", async () => {
    const store = new MemorySessionStore<SessionData>();
    const id = await createSession(store, data);
    const read = await readSession(store, id);

    await store.delete(id);
    const written = await updateSession(store, id, { ...read!, accessToken: "refreshed" });

    expect(written).toBe(false);
    expect(await store.get(id)).toBeNull();
    expect(await readSession(store, id)).toBeNull();
  });

  it("cookie name uses __Host- prefix only for https", () => {
    expect(sessionCookieName("https://gatehouse.example.com")).toBe("__Host-gh_session");
    expect(sessionCookieName("http://localhost:3000")).toBe("gh_session");
  });
});
