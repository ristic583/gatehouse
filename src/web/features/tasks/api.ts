import { apiClient } from "@/lib/api/client";
import type { MeDto, ResolveStaffTaskDto, RevealedPinDto, StaffTaskDto } from "@/lib/api/types";

export type TaskView = "open" | "resolved";

export const tasksApi = {
  list: (view: TaskView) => apiClient.get<StaffTaskDto[]>(`/v1/tasks?status=${view}`),
  claim: (id: string) => apiClient.post<StaffTaskDto>(`/v1/tasks/${id}/claim`),
  resolve: (id: string, body: ResolveStaffTaskDto) => apiClient.post<StaffTaskDto>(`/v1/tasks/${id}/resolve`, body),
  me: () => apiClient.get<MeDto>("/v1/me"),
  /** Only ever called from a mutation: the PIN must never enter the query cache. */
  revealPin: (id: string) => apiClient.post<RevealedPinDto>(`/v1/tasks/${id}/pin-reveal`),
};
