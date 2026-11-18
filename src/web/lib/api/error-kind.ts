import { ApiError } from "./client";

export type ErrorKind = "forbidden" | "other";

/** 403 means "not permitted" (show a message, never retry); everything else is a load error that may be retried. */
export function errorKind(error: unknown): ErrorKind {
  return error instanceof ApiError && error.status === 403 ? "forbidden" : "other";
}
