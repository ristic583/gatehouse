"use client";

import { type Ref } from "react";
import { ArrowUpRight, ChevronRight, Focus, LockKeyhole, ShieldCheck } from "lucide-react";
import Link from "next/link";
import { roomAction } from "../room-action";
import type { AreaAccessDto, AreaDetailsDto } from "@/lib/api/types";
import { CHAIN_TEXT } from "@/features/requests/api";
import { ACCESS_STATE_META, reasonText } from "../access-state";

interface AreaInspectorProps {
  ref: Ref<HTMLElement>;
  selected: AreaAccessDto;
  floorName: string;
  details: { isError: boolean; data: AreaDetailsDto | undefined };
  requestsHref: string;
  canViewRoster: boolean;
  showFocus: boolean;
  onFocus: () => void;
  onRequest: () => void;
}

/** Access details of the selected room with the request/track action. */
export function AreaInspector({
  ref,
  selected,
  floorName,
  details,
  requestsHref,
  canViewRoster,
  showFocus,
  onFocus,
  onRequest,
}: AreaInspectorProps) {
  const state = ACCESS_STATE_META[selected.state];
  const action = roomAction(selected);
  return (
    <section
      ref={ref}
      tabIndex={-1}
      aria-label={`${selected.name} access details`}
      className="gh-panel gh-area-detail"
      key={selected.areaId}
    >
      <div className="gh-panel-title">
        <span className="gh-panel-kicker">02 / ACCESS INTELLIGENCE</span>
        <LockKeyhole size={14} />
      </div>
      <p className="gh-eyebrow">{selected.code}</p>
      <h3>{selected.name}</h3>
      <div className="gh-inspector-tags">
        <span className="gh-pill" data-state={selected.state}>
          <state.icon size={11} />
          {state.label}
        </span>
        <span className="gh-security-tier">
          <ShieldCheck size={11} />
          {selected.securityLevelName}
        </span>
      </div>
      <dl>
        <div>
          <dt>Floor</dt>
          <dd>{floorName}</dd>
        </div>
        <div>
          <dt>Area owner</dt>
          <dd>{details.isError ? "Unavailable" : (details.data?.owners.map((o) => o.displayName).join(", ") ?? "Loading…")}</dd>
        </div>
        <div>
          <dt>Approval policy</dt>
          <dd>{details.data ? CHAIN_TEXT[details.data.securityLevel.approverChain] : "—"}</dd>
        </div>
        <div>
          <dt>Maximum duration</dt>
          <dd>{details.data ? `${details.data.securityLevel.maxDurationDays} days` : "—"}</dd>
        </div>
        <div>
          <dt>Mapped doors</dt>
          <dd>{selected.doors.length.toString().padStart(2, "0")}</dd>
        </div>
      </dl>
      <div className="gh-access-context">
        <state.icon size={13} />
        <p>
          {reasonText(selected.reason, selected.since)}
          {selected.cardInactive ? " · Your card is inactive." : ""}
        </p>
      </div>
      <p className="gh-room-action-help">{action.description}</p>
      {action.kind === "track" ? (
        <Link
          className="gh-primary-link"
          href={`${requestsHref}${requestsHref.includes("?") ? "&" : "?"}area=${encodeURIComponent(selected.areaId)}`}
        >
          Track request
          <ArrowUpRight size={15} />
        </Link>
      ) : (
        <button className="gh-primary-link" onClick={onRequest}>
          {action.label}
          <ArrowUpRight size={15} />
        </button>
      )}
      {canViewRoster && (
        <Link className="gh-focus-room" href={`/areas/${selected.areaId}/roster`}>
          View roster
        </Link>
      )}
      {showFocus && (
        <button className="gh-focus-room" onClick={onFocus}>
          <Focus size={14} /> Focus on this room <ChevronRight size={12} />
        </button>
      )}
    </section>
  );
}
