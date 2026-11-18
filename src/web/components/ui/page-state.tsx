"use client";

import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { errorKind } from "@/lib/api/error-kind";

export type PageStateKind = "loading" | "empty" | "error" | "forbidden";

interface PageStateProps {
  kind: PageStateKind;
  /** Loading: what is loading. Empty: what is missing. Error: what failed. Forbidden: defaults to the standard text. */
  message?: string;
  /** Empty only: the next step for the user. */
  hint?: ReactNode;
  /** Error only: retries the failed load. Never shown for forbidden. */
  onRetry?: () => unknown;
}

export const FORBIDDEN_TEXT = "You do not have access to this page.";

/** The four explicit page states of conventions.md §8: loading, empty, error (with retry) and not permitted (no retry). */
export function PageState({ kind, message, hint, onRetry }: PageStateProps) {
  if (kind === "loading") {
    return (
      <p role="status" className="gh-loading" data-testid="page-state" data-kind={kind}>
        {message ?? "Loading…"}
      </p>
    );
  }
  if (kind === "empty") {
    return (
      <div className="flex flex-col gap-1 text-sm" data-testid="page-state" data-kind={kind}>
        <p className="font-medium">{message ?? "Nothing here yet."}</p>
        {hint !== undefined && <p className="text-muted-foreground">{hint}</p>}
      </div>
    );
  }
  if (kind === "forbidden") {
    return (
      <p role="alert" className="text-sm" data-testid="page-state" data-kind={kind}>
        {message ?? FORBIDDEN_TEXT}
      </p>
    );
  }
  return (
    <div role="alert" className="flex flex-wrap items-center gap-2 text-sm" data-testid="page-state" data-kind={kind}>
      <span>{message ?? "Could not load this page."}</span>
      {onRetry !== undefined && (
        <Button type="button" size="sm" variant="outline" onClick={() => void onRetry()}>
          Retry
        </Button>
      )}
    </div>
  );
}

/** Error state for a failed query: 403 becomes "not permitted" without retry, anything else an error with retry. */
export function QueryErrorState({ error, message, onRetry }: { error: unknown; message: string; onRetry: () => unknown }) {
  return errorKind(error) === "forbidden" ? <PageState kind="forbidden" /> : <PageState kind="error" message={message} onRetry={onRetry} />;
}
