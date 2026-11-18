"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ApiError } from "@/lib/api/client";
import type { StaffTaskDto } from "@/lib/api/types";
import { FINDING_KIND_LABELS } from "@/features/reconciliation/labels";
import { tasksApi, type TaskView } from "../api";
import { noteRequired, resolutionActionsFor } from "../resolution-actions";
import { PinReveal } from "./pin-reveal";

const KIND_LABEL: Record<string, string> = {
  FulfillmentFailed: "Fulfillment failed",
  RevocationUnconfirmed: "Revocation not confirmed",
  ReconciliationFinding: "Reconciliation finding",
  DriftAlert: "Drift alert",
  PinReveal: "PIN reveal",
  ScheduledRemovalRefused: "Scheduled removal refused",
};

export function StaffTaskList({ canHandle }: { canHandle: boolean }) {
  const queryClient = useQueryClient();
  const [view, setView] = useState<TaskView>("open");
  const [resolving, setResolving] = useState<StaffTaskDto | null>(null);
  const [action, setAction] = useState("");
  const [note, setNote] = useState("");
  const tasks = useQuery({ queryKey: ["staff-tasks", view], queryFn: () => tasksApi.list(view), refetchInterval: 10_000 });
  const me = useQuery({ queryKey: ["me"], queryFn: tasksApi.me, staleTime: 60_000 });
  const myPersonId = me.data?.person?.personId ?? null;

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["staff-tasks"] });
  const needsNote = resolving !== null && noteRequired(resolving, action);
  const closeResolve = () => {
    setResolving(null);
    setAction("");
    setNote("");
  };
  const onError = (error: unknown) => toast.error(error instanceof ApiError ? error.detail : "The task could not be updated.");

  const claim = useMutation({
    mutationFn: (id: string) => tasksApi.claim(id),
    onSuccess: async () => {
      toast.success("Task claimed");
      await refresh();
    },
    onError,
  });
  const resolve = useMutation({
    mutationFn: (args: { id: string; action: string; note: string | null }) =>
      tasksApi.resolve(args.id, { action: args.action, note: args.note }),
    onSuccess: async () => {
      toast.success("Task resolved");
      setResolving(null);
      setAction("");
      setNote("");
      await Promise.all([refresh(), queryClient.invalidateQueries({ queryKey: ["fulfillments"] })]);
    },
    onError,
  });

  return (
    <div className="flex flex-col gap-3">
      <div className="flex gap-2">
        <Button size="sm" variant={view === "open" ? "default" : "outline"} aria-pressed={view === "open"} onClick={() => setView("open")}>
          Open
        </Button>
        <Button
          size="sm"
          variant={view === "resolved" ? "default" : "outline"}
          aria-pressed={view === "resolved"}
          onClick={() => setView("resolved")}
        >
          Resolved
        </Button>
      </div>
      {tasks.isError ? (
        <p role="alert">
          {tasks.error instanceof ApiError && tasks.error.status === 403
            ? "You are not permitted to see staff tasks."
            : "Could not load tasks."}
        </p>
      ) : null}
      {tasks.data === undefined ? (
        tasks.isError ? null : (
          <p>Loading…</p>
        )
      ) : tasks.data.length === 0 ? (
        <p>{view === "open" ? "No open tasks." : "No resolved tasks."}</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {tasks.data.map((task) => (
            <li key={task.taskId} data-testid={`task-${task.taskId}`} className="flex flex-col gap-1 rounded-md border p-3 text-sm">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-medium">{KIND_LABEL[task.kind] ?? task.kind}</span>
                {task.findingKind !== undefined && task.findingKind !== null ? (
                  <Badge variant="outline">{FINDING_KIND_LABELS[task.findingKind] ?? task.findingKind}</Badge>
                ) : null}
                <Badge variant="secondary">{task.status}</Badge>
                {task.siteName !== null ? <span>{task.siteName}</span> : null}
              </div>
              <p>{task.summary}</p>
              {task.kind === "PinReveal" && task.personName !== undefined && task.personName !== null ? (
                <p data-testid="task-person">Person: {task.personName}</p>
              ) : null}
              <p>
                {task.assignedTo !== null ? `Assigned to ${task.assignedTo.displayName} · ` : ""}
                {new Date(task.createdAt).toLocaleString()}
              </p>
              {canHandle && task.status !== "Resolved" ? (
                <div className="flex flex-wrap gap-2">
                  {task.status === "Open" ? (
                    <Button size="sm" variant="outline" disabled={claim.isPending} onClick={() => claim.mutate(task.taskId)}>
                      Claim
                    </Button>
                  ) : null}
                  <Button size="sm" onClick={() => setResolving(task)}>
                    Resolve
                  </Button>
                  {task.kind === "PinReveal" &&
                  task.status === "InProgress" &&
                  myPersonId !== null &&
                  task.assignedTo?.personId === myPersonId ? (
                    <PinReveal taskId={task.taskId} />
                  ) : null}
                  {task.subjectType === "Fulfillment" ? (
                    <Link className="text-primary underline" href={`/console?tab=fulfillment&item=${task.subjectId}`}>
                      Open fulfillment
                    </Link>
                  ) : null}
                </div>
              ) : null}
            </li>
          ))}
        </ul>
      )}
      <Dialog open={resolving !== null} onOpenChange={(open) => (open ? undefined : closeResolve())}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Resolve task</DialogTitle>
          </DialogHeader>
          <label htmlFor="resolve-action" className="text-sm">
            Resolution
          </label>
          <select id="resolve-action" className="rounded-md border px-2 py-1" value={action} onChange={(e) => setAction(e.target.value)}>
            <option value="">Select…</option>
            {(resolving === null ? [] : resolutionActionsFor(resolving)).map((r) => (
              <option key={r.value} value={r.value}>
                {r.label}
              </option>
            ))}
          </select>
          <label htmlFor="resolve-note" className="text-sm">
            {needsNote ? "Note (required)" : "Note (optional)"}
          </label>
          <textarea
            id="resolve-note"
            className="rounded-md border px-2 py-1"
            maxLength={500}
            value={note}
            onChange={(e) => setNote(e.target.value)}
          />
          <DialogFooter>
            <Button
              disabled={action === "" || resolving === null || resolve.isPending || (needsNote && note.trim() === "")}
              onClick={() => {
                if (resolving !== null) {
                  resolve.mutate({ id: resolving.taskId, action, note: note.trim() === "" ? null : note.trim() });
                }
              }}
            >
              Confirm resolve
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
