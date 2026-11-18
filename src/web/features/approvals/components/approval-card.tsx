"use client";

import { Check, ShieldCheck, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { formatAccessTime } from "@/features/requests/request-window";
import type { ApprovalInboxItemDto } from "@/lib/api/types";

export const STEP_LABEL: Record<ApprovalInboxItemDto["approverKind"], string> = {
  Manager: "Manager approval",
  AreaOwner: "Area owner approval",
};

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString();
}

interface ApprovalCardProps {
  item: ApprovalInboxItemDto;
  disabled: boolean;
  onReject: () => void;
  onApprove: () => void;
}

/** One pending approval step: person, space, window, justification and the reject/approve buttons. */
export function ApprovalCard({ item, disabled, onReject, onApprove }: ApprovalCardProps) {
  return (
    <article
      aria-label={`${item.subject.displayName} → ${item.area.name}`}
      className="gh-approval-card"

      data-testid={`inbox-item-${item.entitlementId}`}
    >
      <div className="gh-approval-top">
        <span className="gh-avatar">
          {item.subject.displayName
            .split(" ")
            .map((n) => n[0])
            .slice(0, 2)
            .join("")}
        </span>
        <div>
          <strong>{item.subject.displayName}</strong>
          <small>Requested {formatDate(item.createdAt)}</small>
        </div>
        <span className="gh-pill" data-state="Pending">
          {STEP_LABEL[item.approverKind]}
        </span>
      </div>
      <div className="gh-approval-area">
        <div>
          <h2>{item.area.name}</h2>
          <p>
            {item.area.siteName} · {item.area.floorName}
          </p>
        </div>
        <span className="gh-pill">
          <ShieldCheck size={11} /> {item.securityLevelName}
        </span>
      </div>
      {item.requestedBy.personId !== item.subject.personId ? (
        <p className="gh-detail-note">Requested by {item.requestedBy.displayName}</p>
      ) : null}
      <div className="gh-approval-reason">
        <span>WHY ACCESS IS NEEDED</span>
        {item.justification}
      </div>
      <dl className="gh-approval-window">
        <div>
          <dt>Starts</dt>
          <dd>
            <time dateTime={item.requestedStart}>{formatAccessTime(item.requestedStart)}</time>
          </dd>
        </div>
        <div>
          <dt>Ends (exclusive)</dt>
          <dd>
            <time dateTime={item.effectiveEnd}>{formatAccessTime(item.effectiveEnd)}</time>
          </dd>
        </div>
      </dl>
      {item.cappedToEligibility && (
        <p className="gh-policy-note">
          Shortened to the person’s eligibility. Originally requested until {formatAccessTime(item.requestedEnd)}.
        </p>
      )}
      <p className="gh-approval-path">
        Your decision: step {item.stepOrder} · {STEP_LABEL[item.approverKind]}. Access is applied only after all required approvals.
      </p>
      <div className="gh-approval-actions">
        <Button variant="outline" disabled={disabled} onClick={onReject}>
          <X size={14} /> Reject
        </Button>
        <Button className="bg-[#2c543d] text-white hover:bg-[#193d2a]" disabled={disabled} onClick={onApprove}>
          <Check size={14} /> Approve
        </Button>
      </div>
    </article>
  );
}
