"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { ShieldCheck } from "lucide-react";
import { ACCESS_STATE_META, reasonText } from "@/features/map/access-state";
import { roomAction } from "@/features/map/room-action";
import type { AreaAccessDto } from "@/lib/api/types";
import { MY_REQUESTS_KEY, requestsApi } from "../api";
import { formatAccessTime } from "../request-window";
import { statusText } from "./my-requests-table";

interface AreaStateViewProps {
  area: AreaAccessDto;
  myId: string | undefined;
  path: string;
  onClose: () => void;
}

/** Shown instead of the request wizard when the space is not requestable: current state and the user's access records. */
export function AreaStateView({ area, myId, path, onClose }: AreaStateViewProps) {
  const records = useQuery({ queryKey: MY_REQUESTS_KEY, queryFn: requestsApi.mine, enabled: !!myId });
  const matching = (records.data ?? []).filter((r) => r.area.areaId === area.areaId && r.subject.personId === myId);
  return (
    <>
      <p className="gh-dialog-context" data-testid="area-current-state">
        <ShieldCheck size={18} />
        {ACCESS_STATE_META[area.state].label}: {reasonText(area.reason, area.since)}
        {area.cardInactive ? " · Card inactive. Contact your security team." : ""}
      </p>
      <p>{roomAction(area).description}</p>
      {records.isPending && myId && <p role="status">Loading your access records…</p>}
      {records.isError && (
        <p role="alert">
          Access history is unavailable. <button onClick={() => void records.refetch()}>Retry</button>
        </p>
      )}
      {matching.slice(0, 3).map((r) => (
        <div className="gh-access-record" key={r.entitlementId}>
          <strong>{statusText(r)}</strong>
          <p>From {formatAccessTime(r.requestedStart)}</p>
          <p>Until {formatAccessTime(r.effectiveEnd)} (exclusive)</p>
          <p>{r.justification}</p>
        </div>
      ))}
      {records.data && !matching.length && <p>No request history is available for this space. Your map status is shown above.</p>}
      <Link className="gh-primary-link" href={`${path}${encodeURIComponent(area.areaId)}`} onClick={onClose}>
        See request history
      </Link>
    </>
  );
}
