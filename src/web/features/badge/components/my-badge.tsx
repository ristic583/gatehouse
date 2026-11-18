"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api/client";
import { badgeApi, badgeKeys, CARD_STATE_LABELS, PIN_PATTERN } from "../api";
import { PhotoCard } from "./photo-card";

const messageOf = (error: Error | null) => (error instanceof ApiError ? error.detail : error?.message);

/** "My badge": my cards (employees activate their own), set PIN, and ask security staff for PIN help. */
export function MyBadge() {
  const queryClient = useQueryClient();
  const me = useQuery({ queryKey: badgeKeys.me, queryFn: badgeApi.me });
  const cards = useQuery({ queryKey: badgeKeys.cards, queryFn: badgeApi.cards });
  const activate = useMutation({
    mutationFn: badgeApi.activate,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: badgeKeys.cards }),
  });
  // Card activation by the holder is allowed for employees only (ActivateCardRequirement → SelfEmployeeHandler).
  const canActivate = me.data?.person?.type === "Employee";

  return (
    <div className="grid gap-6 md:grid-cols-2">
      <Card>
        <CardHeader>
          <CardTitle>My cards</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-3 text-sm">
          {cards.isError ? <p role="alert">Could not load your cards.</p> : null}
          {cards.data === undefined ? (
            cards.isError ? null : (
              <p>Loading…</p>
            )
          ) : cards.data.length === 0 ? (
            <p>You have no card yet.</p>
          ) : (
            cards.data.map((card) => (
              <div key={card.cardId} data-testid="my-card" className="flex flex-wrap items-center gap-2">
                <span className="font-mono">{card.cardNumberMasked}</span>
                <Badge variant="outline">{CARD_STATE_LABELS[card.state]}</Badge>
                {canActivate && (card.state === "Assigned" || card.state === "Deactivated") ? (
                  <Button size="sm" disabled={activate.isPending} onClick={() => activate.mutate(card.cardId)}>
                    Activate
                  </Button>
                ) : null}
              </div>
            ))
          )}
          {me.data !== undefined && !canActivate ? (
            <p className="text-muted-foreground">Your sponsor or manager activates your card.</p>
          ) : null}
          {activate.error !== null ? <p role="alert">{messageOf(activate.error)}</p> : null}
        </CardContent>
      </Card>
      <PinCard />
      {me.data?.person ? <PhotoCard personId={me.data.person.personId} /> : null}
    </div>
  );
}

function PinCard() {
  const [pin, setPin] = useState("");
  const [saved, setSaved] = useState(false);
  const setPinMutation = useMutation({
    mutationFn: badgeApi.setPin,
    onSuccess: () => {
      setPin("");
      setSaved(true);
    },
  });
  const reveal = useMutation({ mutationFn: badgeApi.requestReveal });
  const valid = PIN_PATTERN.test(pin);

  return (
    <Card>
      <CardHeader>
        <CardTitle>PIN</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3 text-sm">
        <form
          className="flex flex-col gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            if (valid) {
              setSaved(false);
              setPinMutation.mutate(pin);
            }
          }}
        >
          <Label className="flex flex-col items-start gap-1">
            New PIN (4-8 digits)
            <Input
              type="password"
              inputMode="numeric"
              autoComplete="new-password"
              maxLength={8}
              value={pin}
              onChange={(e) => setPin(e.target.value.replace(/\D/g, ""))}
            />
          </Label>
          <Button type="submit" className="self-start" disabled={!valid || setPinMutation.isPending}>
            Set PIN
          </Button>
          {saved ? <p role="status">PIN saved.</p> : null}
          {setPinMutation.error !== null ? <p role="alert">{messageOf(setPinMutation.error)}</p> : null}
        </form>
        <div className="flex flex-col gap-2 border-t pt-3">
          <Button variant="outline" className="self-start" disabled={reveal.isPending || reveal.isSuccess} onClick={() => reveal.mutate()}>
            I forgot my PIN
          </Button>
          {reveal.isSuccess ? <p role="status">Security staff will contact you.</p> : null}
          {reveal.error !== null ? <p role="alert">{messageOf(reveal.error)}</p> : null}
        </div>
      </CardContent>
    </Card>
  );
}
