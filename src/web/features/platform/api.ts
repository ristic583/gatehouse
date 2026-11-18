import { apiClient } from "@/lib/api/client";
import type {
  CreateTenantRequest,
  PlatformAlertDto,
  PlatformOperatorDto,
  PlatformTenantDto,
  PlatformTenantSummaryDto,
} from "@/lib/api/types";

const BASE = "/v1/platform";

export const platformKeys = {
  all: ["platform"] as const,
  me: () => [...platformKeys.all, "me"] as const,
  tenants: () => [...platformKeys.all, "tenants"] as const,
  tenant: (tenantId: string) => [...platformKeys.all, "tenants", tenantId] as const,
  alerts: () => [...platformKeys.all, "alerts"] as const,
};

/** Platform operator API (catalog-only, ADR-009 §2) through the BFF; needs a platform session. */
export const platformApi = {
  me: () => apiClient.get<PlatformOperatorDto>(`${BASE}/me`),
  tenants: () => apiClient.get<PlatformTenantSummaryDto[]>(`${BASE}/tenants`),
  tenant: (tenantId: string) => apiClient.get<PlatformTenantDto>(`${BASE}/tenants/${encodeURIComponent(tenantId)}`),
  create: (body: CreateTenantRequest) => apiClient.post<PlatformTenantDto>(`${BASE}/tenants`, body),
  suspend: (tenantId: string, reason: string) =>
    apiClient.post<PlatformTenantDto>(`${BASE}/tenants/${encodeURIComponent(tenantId)}/suspend`, { reason }),
  reactivate: (tenantId: string) => apiClient.post<PlatformTenantDto>(`${BASE}/tenants/${encodeURIComponent(tenantId)}/reactivate`),
  retry: (tenantId: string) => apiClient.post<PlatformTenantDto>(`${BASE}/tenants/${encodeURIComponent(tenantId)}/provisioning/retry`),
  resendInvite: (tenantId: string) => apiClient.post<void>(`${BASE}/tenants/${encodeURIComponent(tenantId)}/admin-invite`),
  alerts: () => apiClient.get<PlatformAlertDto[]>(`${BASE}/alerts`),
};
