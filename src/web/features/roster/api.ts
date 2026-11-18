import { apiClient } from "@/lib/api/client";
import type { AccessRequestDto, AreaRosterDto } from "@/lib/api/types";

export const rosterApi = {
  roster: (areaId: string) => apiClient.get<AreaRosterDto>(`/v1/areas/${areaId}/roster`),
  revoke: (id: string, reason: string, emergency: boolean) =>
    apiClient.post<AccessRequestDto>(`/v1/access-requests/${id}/${emergency ? "emergency-revoke" : "revoke"}`, { reason }),
};
