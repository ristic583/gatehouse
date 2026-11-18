"use client";

import { useQuery } from "@tanstack/react-query";
import { apiClient } from "./client";
import type { MeDto } from "./types";

export const ME_KEY = ["me"] as const;

export function useMe() {
  return useQuery({ queryKey: ME_KEY, queryFn: () => apiClient.get<MeDto>("/v1/me"), staleTime: 5 * 60_000 });
}
