import type { PlatformTenantDto } from "@/lib/api/types";

export function platformTenant(overrides: Partial<PlatformTenantDto> = {}): PlatformTenantDto {
  return {
    tenantId: "acme",
    displayName: "Acme Corp",
    status: "Active",
    emailDomain: "acme.test",
    idpKind: "Local",
    provisioningStep: "Completed",
    people: 1,
    sites: 0,
    connectorsOnline: 0,
    connectorsOffline: 0,
    alertCount: 0,
    createdAt: "2026-11-14T10:00:00Z",
    provisioning: {
      step: "Completed",
      state: "Completed",
      attempts: 1,
      lastError: null,
      startedAt: "2026-11-14T10:00:00Z",
      completedAt: "2026-11-14T10:01:00Z",
      adminInvitedAt: "2026-11-14T10:01:00Z",
      adminActivatedAt: null,
    },
    statistics: null,
    connectors: [],
    alerts: [],
    suspendedAt: null,
    suspendedReason: null,
    ...overrides,
  };
}
