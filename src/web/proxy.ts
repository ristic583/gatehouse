import { NextResponse, type NextRequest } from "next/server";

const SESSION_COOKIES = ["gh_session", "__Host-gh_session"];
const PUBLIC_PREFIXES = ["/auth/", "/api/proxy/", "/_next/"];
const STATIC_FILE = /\.[a-zA-Z0-9]+$/;
/** Exact public pages (the magic link landing page must work without a session). */
const PUBLIC_PAGES = new Set(["/guest", "/login", "/platform/login"]);
const PLATFORM_SESSION_COOKIES = ["gh_platform_session", "__Host-gh_platform_session"];

/** Optimistic check only: no session cookie → go to sign-in. The session itself is validated server-side. */
export function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;
  if (process.env.NODE_ENV === "development" && pathname === "/design-preview") return NextResponse.next();
  if (PUBLIC_PAGES.has(pathname) || PUBLIC_PREFIXES.some((prefix) => pathname.startsWith(prefix)) || STATIC_FILE.test(pathname)) {
    return NextResponse.next();
  }
  // Platform pages need the operator session; tenant pages ignore the platform cookie (ADR-009 §1).
  if (pathname === "/platform" || pathname.startsWith("/platform/")) {
    if (pathname.startsWith("/platform/auth/") || PLATFORM_SESSION_COOKIES.some((name) => request.cookies.has(name))) {
      return NextResponse.next();
    }
    const platformLogin = new URL("/platform/login", request.url);
    platformLogin.searchParams.set("returnTo", `${pathname}${search}`);
    return NextResponse.redirect(platformLogin);
  }
  if (SESSION_COOKIES.some((name) => request.cookies.has(name))) {
    return NextResponse.next();
  }

  const login = new URL("/login", request.url);
  login.searchParams.set("returnTo", `${pathname}${search}`);
  return NextResponse.redirect(login);
}

export const config = {
  matcher: ["/((?!_next/static|_next/image|favicon.ico).*)"],
};
