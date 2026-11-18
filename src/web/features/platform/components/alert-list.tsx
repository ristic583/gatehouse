"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import type { PlatformAlertDto } from "@/lib/api/types";
import { platformApi, platformKeys } from "../api";
import { SeverityChip } from "./status-chip";

export function AlertListView({ alerts, showTenant = true }: { alerts: readonly PlatformAlertDto[]; showTenant?: boolean }) {
  if (alerts.length === 0) {
    return <p className="text-sm text-muted-foreground">No open alerts.</p>;
  }
  return (
    <ul className="flex flex-col gap-2">
      {alerts.map((alert) => (
        <li
          key={`${alert.tenantId}-${alert.kind}-${alert.since}-${alert.message}`}
          data-testid="platform-alert"
          data-kind={alert.kind}
          className="flex items-center gap-3 rounded-md border px-3 py-2 text-sm"
        >
          <SeverityChip severity={alert.severity} />
          <span className="flex-1">{alert.message}</span>
          {showTenant && (
            <Link className="underline" href={`/platform/tenants/${encodeURIComponent(alert.tenantId)}`}>
              {alert.tenantId}
            </Link>
          )}
          <time className="text-xs text-muted-foreground" dateTime={alert.since}>
            {new Date(alert.since).toLocaleString()}
          </time>
        </li>
      ))}
    </ul>
  );
}

export function AlertList() {
  const query = useQuery({ queryKey: platformKeys.alerts(), queryFn: platformApi.alerts, refetchInterval: 30_000 });
  if (query.isPending) return <p className="gh-loading">Loading alerts…</p>;
  if (query.isError) return <p role="alert">Alerts could not be loaded.</p>;
  return <AlertListView alerts={query.data} />;
}
