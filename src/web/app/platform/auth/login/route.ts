import { connection, NextResponse, type NextRequest } from "next/server";
import { getPlatformAuthConfig } from "@/lib/auth/config";
import { isSameOrigin } from "@/lib/auth/csrf";
import { loginBindingCookieName, loginBindingCookieOptions } from "@/lib/auth/login-binding";
import { beginLogin } from "@/lib/auth/oidc";
import { safePlatformReturnTo } from "@/lib/auth/platform-session";

export async function GET(request: NextRequest) {
  const login = new URL("/platform/login", request.url);
  login.searchParams.set("returnTo", safePlatformReturnTo(request.nextUrl.searchParams.get("returnTo")));
  return NextResponse.redirect(login);
}

/** Starts the operator Authorization Code + PKCE flow against the platform realm (password + OTP in Keycloak). */
export async function POST(request: NextRequest) {
  await connection();
  const config = getPlatformAuthConfig();
  if (!isSameOrigin(request, config.publicUrl)) return new NextResponse("Invalid origin", { status: 403 });
  const form = await request.formData();
  const returnTo = safePlatformReturnTo(typeof form.get("returnTo") === "string" ? String(form.get("returnTo")) : null);
  try {
    const start = await beginLogin(returnTo, undefined, "platform");
    const response = NextResponse.redirect(start.url, 303);
    response.cookies.set(loginBindingCookieName(config.publicUrl, "platform"), start.binding, loginBindingCookieOptions(config.publicUrl));
    return response;
  } catch {
    const login = new URL("/platform/login", config.publicUrl);
    login.searchParams.set("returnTo", returnTo);
    login.searchParams.set("error", "unavailable");
    return NextResponse.redirect(login, 303);
  }
}
