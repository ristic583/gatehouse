import { connection, NextResponse, type NextRequest } from "next/server";
import { getPlatformAuthConfig } from "@/lib/auth/config";
import { loginBindingCookieName } from "@/lib/auth/login-binding";
import { completePlatformLogin } from "@/lib/auth/oidc";
import {
  createPlatformSession,
  PLATFORM_SESSION_MAX_SECONDS,
  platformSessionCookieName,
  safePlatformReturnTo,
} from "@/lib/auth/platform-session";
import { expiredCookieOptions, sessionCookieOptions } from "@/lib/auth/session";
import { getPlatformSessionStore } from "@/lib/auth/stores";

export async function GET(request: NextRequest) {
  await connection();
  const config = getPlatformAuthConfig();
  const currentUrl = new URL(`${request.nextUrl.pathname}${request.nextUrl.search}`, config.publicUrl);

  const bindingCookie = loginBindingCookieName(config.publicUrl, "platform");
  let completed;
  try {
    completed = await completePlatformLogin(currentUrl, request.cookies.get(bindingCookie)?.value);
  } catch {
    const failed = NextResponse.json({ error: "sign_in_failed" }, { status: 400 });
    failed.cookies.set(bindingCookie, "", expiredCookieOptions(config.publicUrl));
    return failed;
  }

  const sessionId = await createPlatformSession(await getPlatformSessionStore(), completed.session);
  const response = NextResponse.redirect(new URL(safePlatformReturnTo(completed.returnTo), config.publicUrl));
  response.cookies.set(
    platformSessionCookieName(config.publicUrl),
    sessionId,
    sessionCookieOptions(config.publicUrl, PLATFORM_SESSION_MAX_SECONDS),
  );
  response.cookies.set(bindingCookie, "", expiredCookieOptions(config.publicUrl));
  return response;
}
