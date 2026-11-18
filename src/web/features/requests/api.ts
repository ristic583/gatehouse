import { apiClient } from "@/lib/api/client";
import type { AccessRequestDto, AreaDetailsDto, CardDto, CreateAccessRequestDto } from "@/lib/api/types";

export const MY_REQUESTS_KEY = ["my-requests"] as const;

export const requestsApi = {
  area: (areaId: string) => apiClient.get<AreaDetailsDto>(`/v1/areas/${areaId}`),
  create: (body: CreateAccessRequestDto) => apiClient.post<AccessRequestDto>("/v1/access-requests", body),
  cancel: (id: string) => apiClient.post<AccessRequestDto>(`/v1/access-requests/${id}/cancel`),
  mine: () => apiClient.get<AccessRequestDto[]>("/v1/access-requests"),
  /** Cards of the request subject: own cards, or a managed/sponsored person's (403 when not allowed). */
  subjectCards: (personId: string | null) => apiClient.get<CardDto[]>(personId === null ? "/v1/me/cards" : `/v1/people/${personId}/cards`),
};

export const CHAIN_TEXT: Record<AreaDetailsDto["securityLevel"]["approverChain"], string> = {
  Manager: "Manager",
  AreaOwner: "Area owner",
  Both: "Manager → Area owner",
};
