"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import { ApiError } from "@/lib/api/client";

/** One retry for network and server errors; a 4xx answer (forbidden, not found, validation) is final. */
export function retryOnce(failureCount: number, error: Error): boolean {
  return failureCount < 1 && !(error instanceof ApiError && error.status >= 400 && error.status < 500);
}

export function QueryProvider({ children }: { children: ReactNode }) {
  const [client] = useState(
    () => new QueryClient({ defaultOptions: { queries: { staleTime: 15_000, retry: retryOnce, refetchOnWindowFocus: false } } }),
  );
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
