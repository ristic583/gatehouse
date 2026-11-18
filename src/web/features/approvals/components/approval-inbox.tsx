"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Search, ShieldCheck } from "lucide-react";
import { EmptyState } from "@/components/flow-steps";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ApiError } from "@/lib/api/client";
import type { ApprovalInboxItemDto } from "@/lib/api/types";
import { approvalsApi, INBOX_KEY } from "../api";
import { ApprovalCard } from "./approval-card";
import { ApprovalReview } from "./approval-review";

export function ApprovalInbox() {
  const queryClient = useQueryClient();
  const inbox = useQuery({ queryKey: INBOX_KEY, queryFn: approvalsApi.inbox });
  const [approving, setApproving] = useState<ApprovalInboxItemDto | null>(null);
  const [decisionError, setDecisionError] = useState<string | null>(null);
  const [kind, setKind] = useState("All");
  const [rejecting, setRejecting] = useState<ApprovalInboxItemDto | null>(null);
  const [comment, setComment] = useState("");
  const [search, setSearch] = useState("");

  const decide = useMutation({
    mutationFn: (args: { item: ApprovalInboxItemDto; action: "approve" | "reject"; comment: string | null }) =>
      approvalsApi.decide(args.item.stepId, args.action, { comment: args.comment }),
    onSuccess: async (_, args) => {
      toast.success(args.action === "approve" ? "Approval recorded. Access still requires system confirmation." : "Request rejected");
      setApproving(null);
      setDecisionError(null);
      setRejecting(null);
      setComment("");
      await Promise.all([INBOX_KEY, ["my-requests"], ["access-map"]].map((queryKey) => queryClient.invalidateQueries({ queryKey })));
    },
    onError: (error: unknown) => {
      const message = error instanceof ApiError ? error.detail : "The decision could not be saved. Try again.";
      setDecisionError(message);
      toast.error(message);
    },
  });

  if (inbox.isError) {
    return <p role="alert">Could not load your approvals.</p>;
  }
  if (inbox.data === undefined) {
    return <p>Loading…</p>;
  }
  if (inbox.data.length === 0) {
    return (
      <EmptyState
        title="Nothing waits for your decision."
        description="You’re all caught up. New requests will appear here when they need your review."
      />
    );
  }

  const filtered = inbox.data
    .filter(
      (item) =>
        (kind === "All" || kind === item.approverKind) &&
        `${item.subject.displayName} ${item.area.name} ${item.area.siteName}`.toLowerCase().includes(search.trim().toLowerCase()),
    )
    .sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt));
  return (
    <div>
      <div className="gh-filterbar">
        <span className="gh-pill">
          <ShieldCheck size={12} /> {inbox.data.length} requests awaiting your decision
        </span>
        <div className="gh-room-search">
          <Search size={13} />
          <input
            aria-label="Search approvals"
            placeholder="Search people or spaces…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
      </div>
      <div className="gh-approval-filter">
        <div className="gh-segmented">
          {[
            ["All", "All decisions"],
            ["Manager", "As manager"],
            ["AreaOwner", "As area owner"],
          ].map(([value, label]) => (
            <button key={value} aria-pressed={kind === value} onClick={() => setKind(value!)}>
              {label}
            </button>
          ))}
        </div>
        <span>Oldest requests first · {filtered.length} shown</span>
      </div>
      <div className="gh-approval-list">
        {filtered.map((item) => (
          <ApprovalCard
            key={item.stepId}
            item={item}
            disabled={decide.isPending}
            onReject={() => {
              setRejecting(item);
              setComment("");
              setDecisionError(null);
            }}
            onApprove={() => {
              setApproving(item);
              setDecisionError(null);
            }}
          />
        ))}
      </div>
      {filtered.length === 0 && <EmptyState title="No matching requests" description="Try a different person or space." />}
      <p className="gh-decision-note">
        <ShieldCheck size={16} />
        Your decision records an approval step. It does not unlock a door or confirm that access has been applied.
      </p>
      <Dialog
        open={approving !== null}
        onOpenChange={(open) => {
          if (!open && !decide.isPending) setApproving(null);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Review approval</DialogTitle>
            <DialogDescription>Confirm the person, space, and exact window before recording your decision.</DialogDescription>
          </DialogHeader>
          {approving && <ApprovalReview item={approving} />}
          {decisionError && <p role="alert">{decisionError}</p>}
          <DialogFooter>
            <Button variant="outline" disabled={decide.isPending} onClick={() => setApproving(null)}>
              Back to inbox
            </Button>
            <Button
              disabled={!approving || decide.isPending}
              onClick={() => {
                if (approving) decide.mutate({ item: approving, action: "approve", comment: null });
              }}
            >
              {decide.isPending ? "Recording…" : "Confirm approval"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <Dialog
        open={rejecting !== null}
        onOpenChange={(open) => {
          if (!open && !decide.isPending) setRejecting(null);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Reject request</DialogTitle>
            <DialogDescription>
              {rejecting?.subject.displayName} · {rejecting?.area.name}. Give the requester a clear reason.
            </DialogDescription>
          </DialogHeader>
          <label htmlFor="reject-comment" className="text-sm">
            Comment
          </label>
          <textarea
            id="reject-comment"
            className="rounded-md border px-2 py-1"
            maxLength={500}
            value={comment}
            onChange={(e) => setComment(e.target.value)}
          />
          {decisionError && <p role="alert">{decisionError}</p>}
          <DialogFooter>
            <Button variant="outline" disabled={decide.isPending} onClick={() => setRejecting(null)}>
              Back to inbox
            </Button>
            <Button
              disabled={comment.trim().length === 0 || decide.isPending || rejecting === null}
              onClick={() => {
                if (rejecting !== null) {
                  decide.mutate({ item: rejecting, action: "reject", comment: comment.trim() });
                }
              }}
            >
              Confirm reject
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
