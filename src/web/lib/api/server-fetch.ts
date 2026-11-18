import "server-only";
import { getAuthConfig } from "@/lib/auth/config";
import { getCurrentSession } from "@/lib/auth/current-session";

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
  ) {
    super(`API request failed with status ${status}${code === undefined ? "" : ` (${code})`}`);
  }
}

export class NotSignedInError extends Error {
  constructor() {
    super("No signed-in session.");
  }
}

/** GET `{API}/api{path}` from a Server Component with the session's access token. */
export async function apiGet<T>(path: string): Promise<T> {
  const session = await getCurrentSession();
  if (session === null) {
    throw new NotSignedInError();
  }

  const response = await fetch(`${getAuthConfig().apiBaseUrl}/api${path}`, {
    headers: { Authorization: `Bearer ${session.data.accessToken}`, Accept: "application/json" },
    cache: "no-store",
  });

  if (!response.ok) {
    let code: string | undefined;
    try {
      const body: unknown = await response.json();
      if (typeof body === "object" && body !== null && "code" in body && typeof body.code === "string") {
        code = body.code;
      }
    } catch {
      code = undefined;
    }
    throw new ApiError(response.status, code);
  }

  return (await response.json()) as T;
}
