import { apiClient } from "@/lib/api/client";
import type { AccessRequestDto, ApprovalInboxItemDto, DecideStepDto } from "@/lib/api/types";

export const INBOX_KEY = ["approvals-inbox"] as const;

export const approvalsApi = {
  inbox: () => apiClient.get<ApprovalInboxItemDto[]>("/v1/approvals/inbox"),
  decide: (stepId: string, action: "approve" | "reject", body: DecideStepDto) =>
    apiClient.post<AccessRequestDto>(`/v1/approvals/${stepId}/${action}`, body),
};
