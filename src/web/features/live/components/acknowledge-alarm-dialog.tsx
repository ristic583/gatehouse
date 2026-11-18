"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ApiError } from "@/lib/api/client";
import type { AlarmDto } from "@/lib/api/types";
import { liveApi, liveKeys } from "../api";
import { ALARM_KIND_META } from "../door-state";

export const MAX_COMMENT_LENGTH = 500;

export function AcknowledgeAlarmDialog({ alarm, onClose }: { alarm: AlarmDto | null; onClose: () => void }) {
  return (
    <Dialog open={alarm !== null} onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent>{alarm !== null ? <AcknowledgeBody key={alarm.alarmId} alarm={alarm} onClose={onClose} /> : null}</DialogContent>
    </Dialog>
  );
}

function AcknowledgeBody({ alarm, onClose }: { alarm: AlarmDto; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [comment, setComment] = useState("");

  const acknowledge = useMutation({
    mutationFn: () => liveApi.acknowledgeAlarm(alarm.alarmId, { comment: comment.trim() }),
    onSuccess: async () => {
      toast.success("Alarm acknowledged");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: liveKeys.alarms() }),
        queryClient.invalidateQueries({ queryKey: liveKeys.floors }),
      ]);
      onClose();
    },
    onError: (e: unknown) => toast.error(e instanceof ApiError ? e.detail : "Could not acknowledge the alarm."),
  });

  const valid = comment.trim().length >= 1 && comment.length <= MAX_COMMENT_LENGTH;
  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (valid) {
      acknowledge.mutate();
    }
  };

  return (
    <>
      <DialogHeader>
        <DialogTitle>Acknowledge alarm</DialogTitle>
        <DialogDescription>
          {ALARM_KIND_META[alarm.kind].label} · {alarm.doorName} · {alarm.areaName}
        </DialogDescription>
      </DialogHeader>
      <form onSubmit={onSubmit} className="flex flex-col gap-3" aria-label="Acknowledge alarm">
        <label htmlFor="alarm-comment" className="text-sm">
          Comment
        </label>
        <textarea
          id="alarm-comment"
          maxLength={MAX_COMMENT_LENGTH}
          className="rounded-md border px-2 py-1"
          value={comment}
          onChange={(e) => setComment(e.target.value)}
        />
        <Button type="submit" disabled={!valid || acknowledge.isPending}>
          Acknowledge
        </Button>
      </form>
    </>
  );
}
