import { apiClient } from "@/lib/api/client";
import type { AuditEntryDto, AuditVerificationDto } from "@/lib/api/types";

export const auditApi = {
  verify: () => apiClient.get<AuditVerificationDto>("/v1/audit/verify"),
  entries: () => apiClient.get<AuditEntryDto[]>("/v1/audit/entries?limit=50"),
};
