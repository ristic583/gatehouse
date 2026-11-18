"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api/client";
import type { OnboardPersonDto, PersonDetailDto } from "@/lib/api/types";
import { peopleApi, peopleKeys } from "../api";
import { errorLabel, ONBOARD_TYPES } from "../labels";

export interface OnboardDialogProps {
  onClose: () => void;
  onCreated: (person: PersonDetailDto) => void;
}

type OnboardType = OnboardPersonDto["type"];

const toIso = (date: string) => new Date(`${date}T00:00:00`).toISOString();

export function OnboardDialog({ onClose, onCreated }: OnboardDialogProps) {
  const queryClient = useQueryClient();
  const [type, setType] = useState<OnboardType>("External");
  const [givenName, setGivenName] = useState("");
  const [familyName, setFamilyName] = useState("");
  const [email, setEmail] = useState("");
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const [cardNumber, setCardNumber] = useState("");
  const onboard = useMutation({
    mutationFn: peopleApi.onboard,
    onSuccess: async (person) => {
      await queryClient.invalidateQueries({ queryKey: peopleKeys.all });
      onCreated(person);
    },
  });

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    onboard.mutate({
      type,
      givenName: givenName.trim(),
      familyName: familyName.trim(),
      email: email.trim(),
      start: toIso(start),
      end: toIso(end),
      timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone,
      sponsorId: null,
      cardNumber: cardNumber.trim() === "" ? null : cardNumber.trim(),
    });
  };

  const error = onboard.error instanceof ApiError ? errorLabel(onboard.error.code, onboard.error.detail) : onboard.error?.message;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Add person"
      data-testid="onboard-dialog"
      className="rounded-lg border bg-background p-4 shadow-lg"
    >
      <form className="flex flex-col gap-3" onSubmit={submit}>
        <h2 className="text-lg font-semibold">Add person</h2>
        <Label className="flex flex-col items-start gap-1">
          Type
          <select
            aria-label="Type"
            className="rounded border px-2 py-1"
            value={type}
            onChange={(e) => setType(e.target.value as OnboardType)}
          >
            {ONBOARD_TYPES.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        </Label>
        <Label className="flex flex-col items-start gap-1">
          Given name
          <Input required value={givenName} onChange={(e) => setGivenName(e.target.value)} />
        </Label>
        <Label className="flex flex-col items-start gap-1">
          Family name
          <Input required value={familyName} onChange={(e) => setFamilyName(e.target.value)} />
        </Label>
        <Label className="flex flex-col items-start gap-1">
          Email
          <Input required type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
        </Label>
        <Label className="flex flex-col items-start gap-1">
          Start
          <Input required type="date" value={start} onChange={(e) => setStart(e.target.value)} />
        </Label>
        <Label className="flex flex-col items-start gap-1">
          End
          <Input required type="date" value={end} onChange={(e) => setEnd(e.target.value)} />
        </Label>
        <Label className="flex flex-col items-start gap-1">
          Card number (optional)
          <Input value={cardNumber} onChange={(e) => setCardNumber(e.target.value)} />
        </Label>
        {error !== undefined ? (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        ) : null}
        <div className="flex gap-2">
          <Button type="submit" disabled={onboard.isPending}>
            Add
          </Button>
          <Button type="button" variant="outline" onClick={onClose}>
            Cancel
          </Button>
        </div>
      </form>
    </div>
  );
}
