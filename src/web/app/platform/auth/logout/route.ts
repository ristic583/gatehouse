import { NextResponse, type NextRequest } from "next/server";
import { getPlatformAuthConfig } from "@/lib/auth/config";
import { isSameOrigin } from "@/lib/auth/csrf";
import { endSessionUrl } from "@/lib/auth/oidc";
import { platformSessionCookieName } from "@/lib/auth/platform-session";
import { expiredCookieOptions } from "@/lib/auth/session";
import { getPlatformSessionStore } from "@/lib/auth/stores";

export async function POST(request: NextRequest) {
  const config = getPlatformAuthConfig();
  if (!isSameOrigin(request, config.publicUrl)) {
    return NextResponse.json({ error: "forbidden" }, { status: 403 });
  }

  const cookieName = platformSessionCookieName(config.publicUrl);
  const sessionId = request.cookies.get(cookieName)?.value;
  let idToken: string | undefined;
  if (sessionId !== undefined) {
    const store = await getPlatformSessionStore();
    idToken = (await store.get(sessionId))?.idToken;
    await store.delete(sessionId);
  }

  const response = NextResponse.redirect(await endSessionUrl(idToken, "platform"), 303);
  response.cookies.set(cookieName, "", expiredCookieOptions(config.publicUrl));
  return response;
}
