import { apiClient } from "@/lib/api/client";
import type { IdentityProviderDto, IdpTestResultDto, SaveIdentityProviderRequest } from "@/lib/api/types";

const BASE = "/v1/admin/identity-provider";

export const identityKeys = { provider: ["admin", "identity-provider"] as const };

export const identityApi = {
  /** 204 (no provider) → null. */
  get: async () => (await apiClient.get<IdentityProviderDto | undefined>(BASE)) ?? null,
  test: (body: SaveIdentityProviderRequest) => apiClient.post<IdpTestResultDto>(`${BASE}/test`, body),
  save: (body: SaveIdentityProviderRequest) => apiClient.put<IdentityProviderDto>(BASE, body),
  remove: () => apiClient.delete<void>(BASE),
};
