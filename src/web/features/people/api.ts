import { apiClient } from "@/lib/api/client";
import type { CardDto, MagicLinkIssuedDto, OnboardPersonDto, PersonDetailDto, PersonSummaryDto } from "@/lib/api/types";

export const peopleKeys = {
  all: ["people"] as const,
  list: (query: string, type: string) => [...peopleKeys.all, "list", query, type] as const,
  detail: (personId: string) => [...peopleKeys.all, "detail", personId] as const,
};

export const peopleApi = {
  list: (query: string, type: string) => {
    const params = new URLSearchParams();
    if (query.trim() !== "") {
      params.set("query", query.trim());
    }
    if (type !== "") {
      params.set("type", type);
    }
    const qs = params.toString();
    return apiClient.get<PersonSummaryDto[]>(`/v1/people${qs === "" ? "" : `?${qs}`}`);
  },
  detail: (personId: string) => apiClient.get<PersonDetailDto>(`/v1/people/${personId}`),
  onboard: (request: OnboardPersonDto) => apiClient.post<PersonDetailDto>("/v1/people", request),
  extend: (personId: string, end: string) => apiClient.put<unknown>(`/v1/people/${personId}/eligibility`, { end }),
  endSponsorship: (personId: string) => apiClient.post<unknown>(`/v1/people/${personId}/sponsorship/end`),
  activateCard: (cardId: string) => apiClient.post<CardDto>(`/v1/cards/${cardId}/activate`),
  deactivateCard: (cardId: string, reason: string) => apiClient.post<CardDto>(`/v1/cards/${cardId}/deactivate`, { reason }),
  issueCard: (personId: string, cardNumber: string) => apiClient.post<CardDto>(`/v1/people/${personId}/cards`, { cardNumber }),
  revokeEntitlement: (entitlementId: string, action: "revoke" | "emergency-revoke", reason: string) =>
    apiClient.post<unknown>(`/v1/access-requests/${entitlementId}/${action}`, { reason }),
  terminate: (personId: string) => apiClient.post<PersonDetailDto>(`/v1/admin/people/${personId}/terminate`),
  issueMagicLink: (personId: string) => apiClient.post<MagicLinkIssuedDto>(`/v1/people/${personId}/magic-links`),
};
