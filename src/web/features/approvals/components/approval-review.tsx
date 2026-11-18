"use client";

import { formatAccessTime } from "@/features/requests/request-window";
import type { ApprovalInboxItemDto } from "@/lib/api/types";
import { STEP_LABEL } from "./approval-card";

/** Summary shown in the approve confirmation: person, space, level, step, window and purpose. */
export function ApprovalReview({ item }: { item: ApprovalInboxItemDto }) {
  return (
    <>
      <dl className="gh-review-grid">
        <div>
          <dt>Person</dt>
          <dd>{item.subject.displayName}</dd>
        </div>
        <div>
          <dt>Space</dt>
          <dd>
            {item.area.name}
            <small>
              {item.area.siteName} · {item.area.floorName}
            </small>
          </dd>
        </div>
        <div>
          <dt>Security level</dt>
          <dd>{item.securityLevelName}</dd>
        </div>
        <div>
          <dt>Your step</dt>
          <dd>
            {item.stepOrder} · {STEP_LABEL[item.approverKind]}
          </dd>
        </div>
        <div>
          <dt>Starts</dt>
          <dd>{formatAccessTime(item.requestedStart)}</dd>
        </div>
        <div>
          <dt>Ends (exclusive)</dt>
          <dd>{formatAccessTime(item.effectiveEnd)}</dd>
        </div>
        <div>
          <dt>Purpose</dt>
          <dd>{item.justification}</dd>
        </div>
      </dl>
      {item.cappedToEligibility && <p className="gh-policy-note">This window is capped to the person’s eligibility.</p>}
      <p className="gh-policy-note">Any remaining approvals and access-system confirmation are still required.</p>
    </>
  );
}
