"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { CapabilityGapList } from "@/features/connectors/components/capability-gap-list";
import { ApiError } from "@/lib/api/client";
import { FULFILLMENTS_KEY, fulfillmentApi, type FulfillmentAction } from "../api";
import { availableActions } from "../actions";

const LABELS: Record<FulfillmentAction, string> = { start: "Start", confirm: "Confirm", fail: "Fail", retry: "Retry" };
const DONE: Record<FulfillmentAction, string> = { start: "Started", confirm: "Confirmed", fail: "Marked as failed", retry: "Retry queued" };

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString();
}

const RELATIVE_UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ["day", 86_400],
  ["hour", 3_600],
  ["minute", 60],
  ["second", 1],
];

function formatRelative(iso: string, now: number = Date.now()): string {
  const seconds = Math.round((new Date(iso).getTime() - now) / 1000);
  const format = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });
  for (const [unit, size] of RELATIVE_UNITS) {
    if (Math.abs(seconds) >= size || unit === "second") {
      return format.format(Math.round(seconds / size), unit);
    }
  }
  return format.format(seconds, "second");
}

export function FulfillmentDetail({ id }: { id: string }) {
  const queryClient = useQueryClient();
  const detail = useQuery({ queryKey: ["fulfillment", id], queryFn: () => fulfillmentApi.detail(id) });
  const [failing, setFailing] = useState(false);
  const [reason, setReason] = useState("");

  const act = useMutation({
    mutationFn: (args: { action: FulfillmentAction; reason?: string }) =>
      fulfillmentApi.act(id, args.action, args.action === "fail" ? { reason: args.reason ?? null } : undefined),
    onSuccess: async (_, args) => {
      toast.success(DONE[args.action]);
      setFailing(false);
      setReason("");
      await Promise.all(
        [FULFILLMENTS_KEY, ["fulfillment", id], ["staff-tasks"]].map((queryKey) => queryClient.invalidateQueries({ queryKey })),
      );
    },
    onError: (error: unknown) => toast.error(error instanceof ApiError ? error.detail : "The action could not be completed."),
  });

  if (detail.isError) {
    return <p role="alert">Could not load the fulfillment.</p>;
  }
  if (detail.data === undefined) {
    return <p>Loading…</p>;
  }

  const { item, desired, confirmed, history, connectorManaged, commandStatus, commandAttempts, nextAttemptAt, lastError, capabilityGaps } =
    detail.data;
  const actions = availableActions(item.status).filter((action) => !connectorManaged || action === "retry");

  return (
    <div className="flex flex-col gap-4" data-testid="fulfillment-detail">
      <div className="flex flex-wrap items-center gap-2">
        <h2 className="text-lg font-semibold">
          {item.kind} · {item.subject.displayName} → {item.area.name}
        </h2>
        {item.priority === "High" ? <Badge variant="destructive">Emergency</Badge> : null}
        <Badge variant="secondary">{item.status}</Badge>
        <span className="text-sm">Revision {item.revision}</span>
      </div>
      {item.status !== "Confirmed" ? (
        <p className="rounded-md border border-amber-500 bg-amber-50 px-3 py-2 text-sm text-amber-900">Desired and confirmed differ</p>
      ) : null}
      <div className="grid gap-4 md:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>Desired state</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-1 text-sm">
            <p>Action: {desired.action}</p>
            <p>Person: {desired.personName}</p>
            <p>
              Access level: {desired.vendorAccessLevelRef} ({desired.accessLevelDisplayName})
            </p>
            <p>
              Valid {formatDateTime(desired.validFrom)} – {formatDateTime(desired.validTo)}
            </p>
            <p>
              {desired.pacsInstanceName} · {desired.vendor}
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>Confirmed state</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-1 text-sm">
            {confirmed === null ? (
              <p>Not confirmed yet</p>
            ) : (
              <>
                <p>
                  Confirmed by {confirmed.confirmedByName ?? confirmed.confirmedById} ({confirmed.confirmedByKind}) at{" "}
                  {formatDateTime(confirmed.confirmedAt)} · server clock
                </p>
                {confirmed.pacsRef !== null ? <p>PACS ref: {confirmed.pacsRef}</p> : null}
              </>
            )}
          </CardContent>
        </Card>
      </div>
      {connectorManaged ? (
        <Card data-testid="connector-status">
          <CardHeader>
            <CardTitle>Handled by connector</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-1 text-sm">
            <p>Command status: {commandStatus ?? "Not dispatched"}</p>
            <p>Attempts: {commandAttempts ?? 0}</p>
            {nextAttemptAt !== null ? <p>Next attempt {formatRelative(nextAttemptAt)}</p> : null}
            {lastError !== null ? <p className="text-destructive">Last error: {lastError}</p> : null}
            {capabilityGaps && capabilityGaps.length > 0 ? (
              <section data-testid="capability-gaps" aria-label="Connector limitations" className="mt-2 flex flex-col gap-1">
                <CapabilityGapList gaps={capabilityGaps} />
                {item.kind === "Grant" && capabilityGaps.includes("NativeValidity") ? <p>End date is enforced by the agent.</p> : null}
              </section>
            ) : null}
          </CardContent>
        </Card>
      ) : null}
      {actions.length > 0 ? (
        <div className="flex gap-2">
          {actions.map((action) => (
            <Button
              key={action}
              variant={action === "fail" ? "outline" : "default"}
              disabled={act.isPending}
              onClick={() => (action === "fail" ? setFailing(true) : act.mutate({ action }))}
            >
              {LABELS[action]}
            </Button>
          ))}
        </div>
      ) : null}
      <div>
        <h3 className="font-medium">History</h3>
        <ul className="text-sm">
          {history.map((entry) => (
            <li key={entry.sequence}>
              {formatDateTime(entry.occurredAt)} · {entry.actorName ?? entry.actorId} · {entry.action}
            </li>
          ))}
        </ul>
      </div>
      <Dialog open={failing} onOpenChange={setFailing}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Mark fulfillment as failed</DialogTitle>
          </DialogHeader>
          <label htmlFor="fail-reason" className="text-sm">
            Reason
          </label>
          <textarea
            id="fail-reason"
            className="rounded-md border px-2 py-1"
            maxLength={500}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />
          <DialogFooter>
            <Button
              disabled={reason.trim().length === 0 || act.isPending}
              onClick={() => act.mutate({ action: "fail", reason: reason.trim() })}
            >
              Confirm fail
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
