"use client";

import { useMutation } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ApiError } from "@/lib/api/client";
import type { DoorCommandResultDto, DoorCommandScope, LiveFloorDto } from "@/lib/api/types";
import { liveApi } from "../api";
import { MAX_REASON_LENGTH } from "./door-actions-dialog";

export type LockdownMode = "start" | "clear";

export const UNSUPPORTED_BY_PACS = "door_command.unsupported_by_pacs";

export function lockdownResultText(result: DoorCommandResultDto): string {
  const skipped = result.skippedDoorCount > 0 ? `, ${result.skippedDoorCount} skipped (not connector-managed or no lockdown support)` : "";
  return `Sent to ${result.doorCount} doors${skipped}`;
}
type LockdownScope = Exclude<DoorCommandScope, "Door">;

export interface LockdownDialogProps {
  mode: LockdownMode | null;
  floor: LiveFloorDto;
  onClose: () => void;
}

export function LockdownDialog({ mode, floor, onClose }: LockdownDialogProps) {
  return (
    <Dialog open={mode !== null} onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent>{mode !== null ? <LockdownBody key={mode} mode={mode} floor={floor} onClose={onClose} /> : null}</DialogContent>
    </Dialog>
  );
}

function LockdownBody({ mode, floor, onClose }: { mode: LockdownMode; floor: LiveFloorDto; onClose: () => void }) {
  const [scope, setScope] = useState<LockdownScope>("Floor");
  const [areaId, setAreaId] = useState(floor.areas[0]?.areaId ?? "");
  const [reason, setReason] = useState("");
  const [confirming, setConfirming] = useState(false);
  const [result, setResult] = useState<DoorCommandResultDto | null>(null);
  const [unsupported, setUnsupported] = useState(false);
  const start = mode === "start";
  const title = start ? "Lockdown" : "Clear lockdown";

  const scopeId = scope === "Site" ? floor.siteId : scope === "Floor" ? floor.floorId : areaId;
  const scopeName =
    scope === "Site" ? floor.siteName : scope === "Floor" ? floor.floorName : (floor.areas.find((a) => a.areaId === areaId)?.name ?? "");

  const send = useMutation({
    mutationFn: () =>
      liveApi.doorCommand({
        kind: start ? "LockdownStart" : "LockdownClear",
        scope,
        scopeId,
        reason: reason.trim(),
        durationSeconds: null,
      }),
    onSuccess: (sent) => {
      toast.success(`${start ? "Lockdown" : "Lockdown clear"} sent to ${sent.doorCount} door(s)`);
      setResult(sent);
    },
    onError: (e: unknown) => {
      setConfirming(false);
      if (e instanceof ApiError && e.status === 409 && e.code === UNSUPPORTED_BY_PACS) {
        setUnsupported(true);
        return;
      }
      toast.error(e instanceof ApiError ? e.detail : "The lockdown command failed.");
    },
  });

  const valid = reason.trim().length >= 1 && reason.length <= MAX_REASON_LENGTH && scopeId !== "";
  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (!valid) {
      return;
    }
    if (!confirming) {
      setConfirming(true);
      return;
    }
    send.mutate();
  };

  if (result !== null) {
    return (
      <>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
        </DialogHeader>
        <p data-testid="lockdown-result" className="text-sm">
          {lockdownResultText(result)}
        </p>
        <Button type="button" onClick={onClose}>
          Done
        </Button>
      </>
    );
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>
          {start ? "Locks every connector-managed door in scope as a priority command." : "Returns doors in scope to normal operation."}
        </DialogDescription>
      </DialogHeader>
      <form onSubmit={onSubmit} className="flex flex-col gap-3" aria-label={title}>
        <label htmlFor="lockdown-scope" className="text-sm">
          Scope
        </label>
        <select
          id="lockdown-scope"
          className="rounded-md border bg-background px-2 py-1"
          value={scope}
          disabled={confirming}
          onChange={(e) => {
            setScope(e.target.value as LockdownScope);
            setUnsupported(false);
          }}
        >
          <option value="Area">Area</option>
          <option value="Floor">Floor</option>
          <option value="Site">Site</option>
        </select>
        {scope === "Area" ? (
          <>
            <label htmlFor="lockdown-area" className="text-sm">
              Area
            </label>
            <select
              id="lockdown-area"
              className="rounded-md border bg-background px-2 py-1"
              value={areaId}
              disabled={confirming}
              onChange={(e) => setAreaId(e.target.value)}
            >
              {floor.areas.map((a) => (
                <option key={a.areaId} value={a.areaId}>
                  {a.name}
                </option>
              ))}
            </select>
          </>
        ) : null}
        <label htmlFor="lockdown-reason" className="text-sm">
          Reason
        </label>
        <textarea
          id="lockdown-reason"
          maxLength={MAX_REASON_LENGTH}
          className="rounded-md border px-2 py-1"
          value={reason}
          disabled={confirming}
          onChange={(e) => setReason(e.target.value)}
        />
        {unsupported ? (
          <p data-testid="lockdown-unsupported" className="text-sm font-medium text-destructive">
            Lockdown is not supported by the PACS in this scope.
          </p>
        ) : null}
        {confirming ? (
          <p role="alert" className="text-sm font-medium text-destructive">
            {start ? "Lock down" : "Clear lockdown for"} {scope.toLowerCase()} {scopeName}?
          </p>
        ) : null}
        <div className="flex gap-2">
          {confirming ? (
            <Button type="button" variant="outline" onClick={() => setConfirming(false)}>
              Back
            </Button>
          ) : null}
          <Button type="submit" variant={start ? "destructive" : "default"} disabled={!valid || send.isPending}>
            {confirming ? `Confirm ${start ? "lockdown" : "clear"}` : "Continue"}
          </Button>
        </div>
      </form>
    </>
  );
}
