"use client";

import { useMutation } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ApiError } from "@/lib/api/client";
import type { LiveDoorDto } from "@/lib/api/types";
import { liveApi } from "../api";
import { DOOR_STATE_META } from "../door-state";

export const MIN_UNLOCK_SECONDS = 1;
export const MAX_UNLOCK_SECONDS = 60;
export const MAX_REASON_LENGTH = 500;

export function isValidDuration(text: string): boolean {
  if (!/^\d+$/.test(text.trim())) {
    return false;
  }
  const value = Number(text);
  return value >= MIN_UNLOCK_SECONDS && value <= MAX_UNLOCK_SECONDS;
}

export function DoorActionsDialog({ door, onClose }: { door: LiveDoorDto | null; onClose: () => void }) {
  return (
    <Dialog open={door !== null} onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent>{door !== null ? <DoorActionsBody key={door.doorId} door={door} onClose={onClose} /> : null}</DialogContent>
    </Dialog>
  );
}

function DoorActionsBody({ door, onClose }: { door: LiveDoorDto; onClose: () => void }) {
  const [duration, setDuration] = useState("5");
  const [reason, setReason] = useState("");

  const unlock = useMutation({
    mutationFn: () =>
      liveApi.doorCommand({
        kind: "MomentaryUnlock",
        scope: "Door",
        scopeId: door.doorId,
        reason: reason.trim(),
        durationSeconds: Number(duration),
      }),
    onSuccess: () => {
      toast.success(`Unlock sent to ${door.name}`);
      onClose();
    },
    onError: (e: unknown) => toast.error(e instanceof ApiError ? e.detail : "The unlock command failed."),
  });

  const durationValid = isValidDuration(duration);
  const valid = durationValid && reason.trim().length >= 1 && reason.length <= MAX_REASON_LENGTH;
  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (valid) {
      unlock.mutate();
    }
  };

  return (
    <>
      <DialogHeader>
        <DialogTitle>{door.name}</DialogTitle>
        <DialogDescription>{DOOR_STATE_META[door.state].label}</DialogDescription>
      </DialogHeader>
      {door.canUnlock ? (
        <form onSubmit={onSubmit} className="flex flex-col gap-3" aria-label="Momentary unlock">
          <label htmlFor="unlock-duration" className="text-sm">
            Duration (seconds, {MIN_UNLOCK_SECONDS}-{MAX_UNLOCK_SECONDS})
          </label>
          <input
            id="unlock-duration"
            type="number"
            min={MIN_UNLOCK_SECONDS}
            max={MAX_UNLOCK_SECONDS}
            className="rounded-md border px-2 py-1"
            value={duration}
            aria-invalid={!durationValid}
            onChange={(e) => setDuration(e.target.value)}
          />
          <label htmlFor="unlock-reason" className="text-sm">
            Reason
          </label>
          <textarea
            id="unlock-reason"
            maxLength={MAX_REASON_LENGTH}
            className="rounded-md border px-2 py-1"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />
          <Button type="submit" disabled={!valid || unlock.isPending}>
            Momentary unlock
          </Button>
        </form>
      ) : (
        <p data-testid="unlock-unsupported" className="text-sm text-muted-foreground">
          This door&apos;s PACS does not support remote unlock.
        </p>
      )}
    </>
  );
}
