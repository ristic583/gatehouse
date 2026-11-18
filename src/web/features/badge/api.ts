import { apiClient } from "@/lib/api/client";
import type { CardDto, MeDto, PinRevealRequestedDto } from "@/lib/api/types";

export const badgeKeys = {
  me: ["me"] as const,
  cards: ["badge", "cards"] as const,
};

export const badgeApi = {
  me: () => apiClient.get<MeDto>("/v1/me"),
  cards: () => apiClient.get<CardDto[]>("/v1/me/cards"),
  activate: (cardId: string) => apiClient.post<CardDto>(`/v1/cards/${cardId}/activate`),
  setPin: (pin: string) => apiClient.put<undefined>("/v1/me/pin", { pin }),
  requestReveal: () => apiClient.post<PinRevealRequestedDto>("/v1/me/pin/reveal-requests"),
};

export const PIN_PATTERN = /^\d{4,8}$/;

export const CARD_STATE_LABELS: Record<CardDto["state"], string> = {
  Assigned: "Assigned, not active yet",
  ActivationPending: "Activation in progress",
  Active: "Active",
  Deactivated: "Deactivated",
  Revoked: "Revoked",
};
