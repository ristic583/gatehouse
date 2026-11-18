import { NextResponse, type NextRequest } from "next/server";
import { getAuthConfig } from "@/lib/auth/config";
import { isSameOrigin } from "@/lib/auth/csrf";
import { endSessionUrl } from "@/lib/auth/oidc";
import { expiredCookieOptions, sessionCookieName } from "@/lib/auth/session";
import { getSessionStore } from "@/lib/auth/stores";

export async function POST(request: NextRequest) {
  const config = getAuthConfig();
  if (!isSameOrigin(request, config.publicUrl)) {
    return NextResponse.json({ error: "forbidden" }, { status: 403 });
  }

  const cookieName = sessionCookieName(config.publicUrl);
  const sessionId = request.cookies.get(cookieName)?.value;
  let idToken: string | undefined;
  if (sessionId !== undefined) {
    const store = await getSessionStore();
    idToken = (await store.get(sessionId))?.idToken;
    await store.delete(sessionId);
  }

  const response = NextResponse.redirect(await endSessionUrl(idToken), 303);
  response.cookies.set(cookieName, "", expiredCookieOptions(config.publicUrl));
  return response;
}
