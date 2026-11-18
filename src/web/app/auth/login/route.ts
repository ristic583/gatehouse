import { connection, NextResponse, type NextRequest } from "next/server";
import { isSameOrigin } from "@/lib/auth/csrf";
import { loginBindingCookieName, loginBindingCookieOptions } from "@/lib/auth/login-binding";
import { beginLogin } from "@/lib/auth/oidc";
import { clientKey, loginLimiter } from "@/lib/auth/rate-limit";
import { safeReturnTo } from "./return-to";

/** All unauthenticated entry points first show Gatehouse's branded email screen. */
export async function GET(request: NextRequest) {
  const login = new URL("/login", request.url);
  login.searchParams.set("returnTo", safeReturnTo(request.nextUrl.searchParams.get("returnTo")));
  return NextResponse.redirect(login);
}

export async function POST(request: NextRequest) {
  await connection();
  // Prevent cross-origin form submissions; email is a hint, never a tenant selector.
  if (!isSameOrigin(request, process.env.GATEHOUSE_PUBLIC_URL ?? request.nextUrl.origin))
    return new NextResponse("Invalid origin", { status: 403 });
  // SEC-05: each attempt stores a login transaction in Redis; limit per client.
  if (!loginLimiter.tryAcquire(clientKey(request)))
    return new NextResponse("Too many sign-in attempts. Try again in a minute.", { status: 429, headers: { "retry-after": "60" } });
  const form = await request.formData();
  const returnTo = safeReturnTo(typeof form.get("returnTo") === "string" ? String(form.get("returnTo")) : null);
  const email = String(form.get("email") ?? "")
    .trim()
    .toLowerCase();
  const login = new URL("/login", request.url);
  login.searchParams.set("returnTo", returnTo);
  if (email.length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
    login.searchParams.set("error", "email");
    return NextResponse.redirect(login, 303);
  }
  try {
    const start = await beginLogin(returnTo, email);
    const publicUrl = process.env.GATEHOUSE_PUBLIC_URL ?? request.nextUrl.origin;
    const response = NextResponse.redirect(start.url, 303);
    response.cookies.set(loginBindingCookieName(publicUrl, "tenant"), start.binding, loginBindingCookieOptions(publicUrl));
    return response;
  } catch {
    login.searchParams.set("error", "unavailable");
    return NextResponse.redirect(login, 303);
  }
}
