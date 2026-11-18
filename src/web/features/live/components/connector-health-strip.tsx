"use client";

import { CapabilityGapList } from "@/features/connectors/components/capability-gap-list";
import type { ConnectorHealthDto } from "@/lib/api/types";

export function relativeTime(iso: string | null, now: number): string {
  if (iso === null) {
    return "never";
  }
  const seconds = Math.max(0, Math.round((now - new Date(iso).getTime()) / 1000));
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 48) return `${hours}h ago`;
  return `${Math.round(hours / 24)}d ago`;
}

export interface ConnectorHealthStripProps {
  connectors: readonly ConnectorHealthDto[];
  /** Epoch ms used for relative times (ticked by the parent). */
  now: number;
}

export function ConnectorHealthStrip({ connectors, now }: ConnectorHealthStripProps) {
  if (connectors.length === 0) {
    return null;
  }
  return (
    <ul aria-label="Connector health" className="flex flex-wrap gap-2 text-sm">
      {connectors.map((c) => {
        const manual = c.fulfillmentMode === "Manual";
        const badge = manual ? "Manual" : c.online ? "Online" : "Offline";
        return (
          <li
            key={c.pacsInstanceId}
            data-testid="connector-health"
            data-online={c.online}
            className="flex items-center gap-2 rounded-md border px-3 py-1"
          >
            <span className="font-medium">{c.name}</span>
            <span
              data-testid="connector-badge"
              className={
                manual
                  ? "rounded bg-muted px-1.5 text-xs"
                  : c.online
                    ? "rounded bg-green-100 px-1.5 text-xs text-green-800"
                    : "rounded bg-red-100 px-1.5 text-xs text-red-800"
              }
            >
              {badge}
            </span>
            {manual ? null : (
              <span className="text-muted-foreground">
                heartbeat {relativeTime(c.lastHeartbeatAt, now)} · queue {c.queuedCommands} · ack{" "}
                {c.lastAckLatencyMs === null ? "–" : `${c.lastAckLatencyMs} ms`}
                {c.pacsReachable === false ? " · PACS unreachable" : ""}
              </span>
            )}
            {manual ? null : <CapabilityGapList gaps={c.capabilityGaps} />}
          </li>
        );
      })}
    </ul>
  );
}
