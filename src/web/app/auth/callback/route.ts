import { connection, NextResponse, type NextRequest } from "next/server";
import { getAuthConfig } from "@/lib/auth/config";
import { loginBindingCookieName } from "@/lib/auth/login-binding";
import { completeLogin } from "@/lib/auth/oidc";
import { createSession, expiredCookieOptions, sessionCookieName, sessionCookieOptions } from "@/lib/auth/session";
import { getSessionStore } from "@/lib/auth/stores";

export async function GET(request: NextRequest) {
  await connection();
  const config = getAuthConfig();
  const currentUrl = new URL(`${request.nextUrl.pathname}${request.nextUrl.search}`, config.publicUrl);
  if (!currentUrl.searchParams.has("code") && !currentUrl.searchParams.has("error")) {
    // Landing here without a sign-in response (e.g. Keycloak's "back to application" after an invitation): start at home.
    return NextResponse.redirect(new URL("/", config.publicUrl));
  }

  const bindingCookie = loginBindingCookieName(config.publicUrl, "tenant");
  let completed;
  try {
    completed = await completeLogin(currentUrl, request.cookies.get(bindingCookie)?.value);
  } catch {
    const failed = NextResponse.json({ error: "sign_in_failed" }, { status: 400 });
    failed.cookies.set(bindingCookie, "", expiredCookieOptions(config.publicUrl));
    return failed;
  }

  const sessionId = await createSession(await getSessionStore(), completed.session);
  const response = NextResponse.redirect(new URL(completed.returnTo, config.publicUrl));
  response.cookies.set(sessionCookieName(config.publicUrl), sessionId, sessionCookieOptions(config.publicUrl));
  response.cookies.set(bindingCookie, "", expiredCookieOptions(config.publicUrl));
  return response;
}
