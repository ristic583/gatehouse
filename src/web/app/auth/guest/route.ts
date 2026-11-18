import { NextResponse, type NextRequest } from "next/server";
import { getAuthConfig } from "@/lib/auth/config";
import { isSameOrigin } from "@/lib/auth/csrf";
import { redeemGuestToken } from "@/lib/auth/guest";
import { createSession, sessionCookieName, sessionCookieOptions, sessionTtl } from "@/lib/auth/session";
import { getSessionStore } from "@/lib/auth/stores";

/** Form POST from `/guest`: redeems the magic link server-side and starts a 30-minute guest session (no refresh token). */
export async function POST(request: NextRequest) {
  const config = getAuthConfig();
  if (!isSameOrigin(request, config.publicUrl)) {
    return NextResponse.json({ error: "forbidden" }, { status: 403 });
  }

  const form = await request.formData();
  const token = form.get("token");
  const session = typeof token === "string" ? await redeemGuestToken(config.apiBaseUrl, token) : null;
  if (session === null) {
    return NextResponse.redirect(new URL("/guest?error=invalid", config.publicUrl), 303);
  }

  const sessionId = await createSession(await getSessionStore(), session);
  const response = NextResponse.redirect(new URL("/map", config.publicUrl), 303);
  response.cookies.set(sessionCookieName(config.publicUrl), sessionId, sessionCookieOptions(config.publicUrl, sessionTtl(session)));
  return response;
}
