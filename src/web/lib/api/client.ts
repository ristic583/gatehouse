/** Browser-side API client against the BFF proxy (`/api/proxy`). The browser never sees tokens. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    readonly detail: string,
  ) {
    super(detail);
    this.name = "ApiError";
  }
}

async function parseError(response: Response): Promise<ApiError> {
  let code: string | undefined;
  let detail = `Request failed (${response.status}).`;
  try {
    const body: unknown = await response.json();
    if (typeof body === "object" && body !== null) {
      if ("code" in body && typeof body.code === "string") {
        code = body.code;
      }
      if ("detail" in body && typeof body.detail === "string") {
        detail = body.detail;
      } else if ("title" in body && typeof body.title === "string") {
        detail = body.title;
      }
    }
  } catch {
    // Non-JSON error body: keep the generic detail.
  }
  return new ApiError(response.status, code, detail);
}

async function send<T>(method: string, path: string, body?: unknown): Promise<T> {
  const response = await fetch(`/api/proxy${path}`, {
    method,
    headers: body === undefined ? { Accept: "application/json" } : { Accept: "application/json", "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: "same-origin",
  });
  if (!response.ok) {
    throw await parseError(response);
  }
  if (response.status === 204) {
    return undefined as T;
  }
  return (await response.json()) as T;
}

export const apiClient = {
  get: <T>(path: string): Promise<T> => send<T>("GET", path),
  post: <T>(path: string, body?: unknown): Promise<T> => send<T>("POST", path, body ?? {}),
  put: <T>(path: string, body: unknown): Promise<T> => send<T>("PUT", path, body),
  delete: <T>(path: string): Promise<T> => send<T>("DELETE", path),
  /** Multipart upload; the browser sets the boundary. */
  upload: async <T>(method: "POST" | "PUT", path: string, form: FormData): Promise<T> => {
    const response = await fetch(`/api/proxy${path}`, {
      method,
      body: form,
      credentials: "same-origin",
      headers: { Accept: "application/json" },
    });
    if (!response.ok) {
      throw await parseError(response);
    }
    return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
  },
};
