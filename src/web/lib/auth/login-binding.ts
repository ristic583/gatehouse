import { createHash, randomBytes, timingSafeEqual } from "node:crypto";

/**
 * Browser binding of an OIDC login transaction (GH-AUTH-01): the browser that starts a login gets a random secret in a short-lived
 * httpOnly cookie, the transaction stores only its hash, and the callback must present the same secret. A callback URL opened in
 * another browser (login CSRF with the attacker's own code) is refused.
 */
export const LOGIN_BINDING_TTL_SECONDS = 600;

export type LoginBindingKind = "tenant" | "platform";

export function newLoginBinding(): { value: string; hash: string } {
  const value = randomBytes(32).toString("base64url");
  return { value, hash: hashLoginBinding(value) };
}

export function hashLoginBinding(value: string): string {
  return createHash("sha256").update(value).digest("base64url");
}

/** True only when the presented cookie value hashes to the hash stored with the transaction. */
export function loginBindingMatches(expectedHash: string | undefined, presented: string | undefined): boolean {
  if (expectedHash === undefined || presented === undefined || presented === "") {
    return false;
  }
  const expected = Buffer.from(expectedHash);
  const actual = Buffer.from(hashLoginBinding(presented));
  return expected.length === actual.length && timingSafeEqual(expected, actual);
}

export function loginBindingCookieName(publicUrl: string, kind: LoginBindingKind): string {
  const name = kind === "platform" ? "gh_platform_login" : "gh_login";
  return publicUrl.startsWith("https://") ? `__Host-${name}` : name;
}

export function loginBindingCookieOptions(publicUrl: string) {
  return {
    httpOnly: true,
    sameSite: "lax" as const,
    secure: publicUrl.startsWith("https://"),
    path: "/",
    maxAge: LOGIN_BINDING_TTL_SECONDS,
  };
}
