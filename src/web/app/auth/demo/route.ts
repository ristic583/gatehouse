import { connection, NextResponse, type NextRequest } from "next/server";
import { getAuthConfig } from "@/lib/auth/config";
import { isSameOrigin } from "@/lib/auth/csrf";
import { isDemoPersona } from "@/lib/auth/demo-personas";
import { isDemoSignInEnabled, signInAsDemoPersona } from "@/lib/auth/demo-sign-in";
import { clientKey, loginLimiter } from "@/lib/auth/rate-limit";
import { createSession, sessionCookieName, sessionCookieOptions } from "@/lib/auth/session";
import { getSessionStore } from "@/lib/auth/stores";
import { safeReturnTo } from "../login/return-to";

/** Form POST from the login page of the public demo: signs the chosen demo persona in (`lib/auth/demo-sign-in.ts`). */
export async function POST(request: NextRequest) {
  await connection();
  if (!isDemoSignInEnabled()) return new NextResponse("Not found", { status: 404 });
  const config = getAuthConfig();
  if (!isSameOrigin(request, config.publicUrl)) return new NextResponse("Invalid origin", { status: 403 });
  if (!loginLimiter.tryAcquire(clientKey(request)))
    return new NextResponse("Too many sign-in attempts. Try again in a minute.", { status: 429, headers: { "retry-after": "60" } });

  const form = await request.formData();
  const persona = String(form.get("persona") ?? "");
  const returnTo = safeReturnTo(typeof form.get("returnTo") === "string" ? String(form.get("returnTo")) : null);
  const login = new URL("/login", config.publicUrl);
  if (!isDemoPersona(persona)) {
    login.searchParams.set("error", "persona");
    return NextResponse.redirect(login, 303);
  }
  try {
    const completed = await signInAsDemoPersona(persona, returnTo, `${config.publicUrl}/auth/callback`);
    const sessionId = await createSession(await getSessionStore(), completed.session);
    const response = NextResponse.redirect(new URL(completed.returnTo, config.publicUrl), 303);
    response.cookies.set(sessionCookieName(config.publicUrl), sessionId, sessionCookieOptions(config.publicUrl));
    return response;
  } catch {
    login.searchParams.set("error", "unavailable");
    return NextResponse.redirect(login, 303);
  }
}
