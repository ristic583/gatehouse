/** True when the request's Origin header equals the public origin of the app. Missing Origin is rejected. */
export function isSameOrigin(request: Request, publicUrl: string): boolean {
  const origin = request.headers.get("origin");
  if (origin === null || origin === "" || origin === "null") {
    return false;
  }
  try {
    return new URL(origin).origin === new URL(publicUrl).origin;
  } catch {
    return false;
  }
}
