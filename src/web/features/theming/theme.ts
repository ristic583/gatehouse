"use client";

import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@/lib/api/client";
import type { TermKey, ThemeDto } from "@/lib/api/types";

export const THEME_KEY = ["theme"] as const;

export const themeApi = {
  get: () => apiClient.get<ThemeDto>("/v1/theme"),
  save: (theme: ThemeDto) => apiClient.put<ThemeDto>("/v1/admin/theme", theme),
};

export function useTheme() {
  return useQuery({ queryKey: THEME_KEY, queryFn: themeApi.get, staleTime: 5 * 60_000 });
}

/** The tenant's word for a noun ("Area" → "Zone"), or the noun itself. */
export function useTerm(key: TermKey): string {
  const theme = useTheme();
  // Defensive: a stale or unexpected payload must never break the page that only wants a label.
  const terms = theme.data?.terms as ThemeDto["terms"] | undefined;
  return terms?.[key] ?? key;
}
