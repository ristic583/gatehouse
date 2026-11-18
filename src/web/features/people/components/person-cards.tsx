"use client";

import { useMutation } from "@tanstack/react-query";
import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { CardDto } from "@/lib/api/types";
import { peopleApi } from "../api";
import { messageOf } from "../labels";

export function PersonPhoto({ personId, name }: { personId: string; name: string }) {
  const [visible, setVisible] = useState(true);
  return visible ? (
    // eslint-disable-next-line @next/next/no-img-element -- private, uncached image through the BFF; each view is audited
    <img
      data-testid="person-photo"
      src={`/api/proxy/v1/people/${personId}/photo`}
      alt={`Photo of ${name}`}
      className="h-20 w-20 rounded-md object-cover"
      onError={() => setVisible(false)}
    />
  ) : null;
}

export function CardRow({
  card,
  canActivate,
  canDeactivate,
  onDone,
}: {
  card: CardDto;
  canActivate: boolean;
  canDeactivate: boolean;
  onDone: () => unknown;
}) {
  const [reason, setReason] = useState<string | null>(null);
  const activate = useMutation({ mutationFn: () => peopleApi.activateCard(card.cardId), onSuccess: onDone });
  const deactivate = useMutation({
    mutationFn: (why: string) => peopleApi.deactivateCard(card.cardId, why),
    onSuccess: () => {
      setReason(null);
      return onDone();
    },
  });
  const error = messageOf(activate.error ?? deactivate.error);

  return (
    <div data-testid="card-row" className="flex flex-wrap items-center gap-2">
      <span className="font-mono">{card.cardNumberMasked}</span>
      <Badge variant="outline">{card.state}</Badge>
      {card.pacsStatus !== null ? <span data-testid="card-pacs-status">PACS: {card.pacsStatus}</span> : null}
      {canActivate && (card.state === "Assigned" || card.state === "Deactivated") ? (
        <Button size="sm" disabled={activate.isPending} onClick={() => activate.mutate()}>
          Activate
        </Button>
      ) : null}
      {canDeactivate && card.state === "Active" && reason === null ? (
        <Button size="sm" variant="outline" onClick={() => setReason("")}>
          Deactivate
        </Button>
      ) : null}
      {reason !== null ? (
        <form
          className="flex items-center gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            deactivate.mutate(reason.trim());
          }}
        >
          <Input aria-label="Deactivation reason" required value={reason} onChange={(e) => setReason(e.target.value)} />
          <Button size="sm" type="submit" variant="destructive" disabled={deactivate.isPending || reason.trim() === ""}>
            Confirm deactivate
          </Button>
        </form>
      ) : null}
      {error !== undefined ? (
        <p role="alert" className="w-full text-destructive">
          {error}
        </p>
      ) : null}
    </div>
  );
}

export function IssueCardForm({ personId, onDone }: { personId: string; onDone: () => unknown }) {
  const [number, setNumber] = useState("");
  const issue = useMutation({
    mutationFn: (cardNumber: string) => peopleApi.issueCard(personId, cardNumber),
    onSuccess: () => {
      setNumber("");
      return onDone();
    },
  });
  const error = messageOf(issue.error);
  return (
    <form
      className="flex flex-wrap items-center gap-2"
      data-testid="issue-card"
      onSubmit={(e) => {
        e.preventDefault();
        issue.mutate(number.trim());
      }}
    >
      <Input aria-label="Card number" required className="max-w-[14rem]" value={number} onChange={(e) => setNumber(e.target.value)} />
      <Button size="sm" type="submit" disabled={issue.isPending || number.trim() === ""}>
        Issue card
      </Button>
      {error !== undefined ? (
        <p role="alert" className="w-full text-destructive">
          {error}
        </p>
      ) : null}
    </form>
  );
}
