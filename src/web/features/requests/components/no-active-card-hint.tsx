"use client";

import { useQuery } from "@tanstack/react-query";
import { requestsApi } from "../api";

export const NO_ACTIVE_CARD_TEXT = "No active card: access applies once a card is issued and activated";

/**
 * Lab #6: warns at request time that a grant cannot reach the PACS before the subject has an active card. `personId` null =
 * the signed-in user (`/me/cards`); otherwise the managed or sponsored person. A refusal (403) or any error shows no hint.
 */
export function NoActiveCardHint({ personId }: { personId: string | null }) {
  const cards = useQuery({
    queryKey: ["request-subject-cards", personId ?? "me"],
    queryFn: () => requestsApi.subjectCards(personId),
    retry: false,
  });
  if (!cards.isSuccess || !Array.isArray(cards.data) || cards.data.some((card) => card.state === "Active")) return null;
  return (
    <p role="status" className="text-sm text-muted-foreground" data-testid="no-active-card-hint">
      {NO_ACTIVE_CARD_TEXT}
    </p>
  );
}
