import "server-only";
import type { SessionData } from "./session";

/** Mirrors Gatehouse.Application.Identity.MagicLinks.GuestSessionDto. */
interface GuestSessionDto {
  accessToken: string;
  expiresAt: string;
  sub: string;
  email: string;
  name: string;
  tenantId: string;
}

/** Redeems a magic link token at the API (server-side). Null for any failure: the API answers every invalid token the same way. */
export async function redeemGuestToken(apiBaseUrl: string, token: string): Promise<SessionData | null> {
  if (token === "" || token.length > 512) {
    return null;
  }
  const response = await fetch(`${apiBaseUrl}/api/v1/guest/sessions`, {
    method: "POST",
    headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify({ token }),
    cache: "no-store",
  });
  if (!response.ok) {
    return null;
  }
  const body = (await response.json()) as GuestSessionDto;
  return toGuestSession(body);
}

export function toGuestSession(body: GuestSessionDto): SessionData {
  return {
    sub: body.sub,
    email: body.email,
    name: body.name,
    tenantId: body.tenantId,
    accessToken: body.accessToken,
    accessTokenExpiresAt: Math.floor(new Date(body.expiresAt).getTime() / 1000),
    guest: true,
  };
}
