"use client";

import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { SiteFloorPicker } from "@/features/map/components/site-floor-picker";
import { apiClient } from "@/lib/api/client";
import { useMe } from "@/lib/api/me";
import { Button } from "@/components/ui/button";
import { PageState, QueryErrorState } from "@/components/ui/page-state";
import type { LiveDoorDto, SiteDto } from "@/lib/api/types";
import { liveApi, liveKeys } from "../api";
import { useLiveSite } from "../hooks/use-live-site";
import { AlarmPanel } from "./alarm-panel";
import { ConnectorHealthStrip } from "./connector-health-strip";
import { DoorActionsDialog } from "./door-actions-dialog";
import { EventFeed } from "./event-feed";
import { LiveFloorPlan } from "./live-floor-plan";
import { ReplayScrubber } from "./replay-scrubber";
import { LockdownDialog, type LockdownMode } from "./lockdown-dialog";

const LIVE_ROLES = ["SecurityStaff", "TenantAdmin"];

const STATUS_LABEL = {
  connecting: "Connecting…",
  connected: "Live",
  reconnecting: "Reconnecting…",
  disconnected: "Offline",
  forbidden: "Not permitted",
} as const;

function useNow(intervalMs: number): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(id);
  }, [intervalMs]);
  return now;
}

export function LiveView() {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const me = useMe();
  const permitted = me.data?.roles.some((r) => LIVE_ROLES.includes(r)) ?? false;
  // Tenant admins see the live view read-only; only security staff act (spec §6.2).
  const canAct = me.data?.roles.includes("SecurityStaff") ?? false;
  const [selectedDoor, setSelectedDoor] = useState<LiveDoorDto | null>(null);
  const [replay, setReplay] = useState(false);
  const [lockdown, setLockdown] = useState<LockdownMode | null>(null);

  const sites = useQuery({
    queryKey: ["sites"],
    queryFn: () => apiClient.get<SiteDto[]>("/v1/sites"),
    enabled: permitted,
  });
  const siteList = sites.data ?? [];
  const site = siteList.find((s) => s.siteId === params.get("site")) ?? siteList[0];
  const floor = site?.floors.find((f) => f.floorId === params.get("floor")) ?? site?.floors[0];
  const floorId = floor?.floorId;
  const siteId = site?.siteId;

  const liveFloor = useQuery({
    queryKey: liveKeys.floor(floorId ?? ""),
    queryFn: () => liveApi.floor(floorId ?? ""),
    enabled: permitted && floorId !== undefined,
  });
  const connectors = useQuery({
    queryKey: liveKeys.connectors(siteId),
    queryFn: () => liveApi.connectors(siteId),
    enabled: permitted && siteId !== undefined,
  });
  const alarms = useQuery({
    queryKey: liveKeys.alarms("open", siteId),
    queryFn: () => liveApi.alarms("open", siteId),
    enabled: permitted && siteId !== undefined,
  });
  const status = useLiveSite(permitted ? (siteId ?? null) : null);
  const now = useNow(5_000);

  if (me.data === undefined) {
    return me.isError ? (
      <QueryErrorState error={me.error} message="Could not load your profile." onRetry={() => me.refetch()} />
    ) : (
      <PageState kind="loading" message="Loading the live building…" />
    );
  }
  if (!permitted || status === "forbidden") {
    return <PageState kind="forbidden" message="You are not permitted to view the live building." />;
  }
  if (sites.isError) {
    return <QueryErrorState error={sites.error} message="Could not load sites." onRetry={() => sites.refetch()} />;
  }
  if (sites.data === undefined) {
    return <PageState kind="loading" message="Loading sites…" />;
  }
  if (sites.data.length === 0) {
    return <PageState kind="empty" message="No sites yet." hint="A tenant admin adds sites and floor plans under Admin → Sites." />;
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-4">
        {site !== undefined && floorId !== undefined ? (
          <SiteFloorPicker
            sites={siteList}
            siteId={site.siteId}
            floorId={floorId}
            onChange={(s, f) => router.replace(`${pathname}?site=${s}&floor=${f}`)}
          />
        ) : null}
        {canAct && liveFloor.data !== undefined ? (
          <div className="flex gap-2">
            {liveFloor.data.doors.some((d) => d.state === "Lockdown") ? (
              <Button variant="outline" onClick={() => setLockdown("clear")}>
                Clear lockdown
              </Button>
            ) : null}
            <Button variant="destructive" size="lg" onClick={() => setLockdown("start")}>
              Lockdown
            </Button>
          </div>
        ) : null}
        <span data-testid="live-status" data-status={status} className="text-sm text-muted-foreground">
          {STATUS_LABEL[status]}
        </span>
      </div>
      <ConnectorHealthStrip connectors={connectors.data ?? []} now={now} />
      {liveFloor.data !== undefined && isMassUnlock(liveFloor.data.doors) ? (
        <p
          role="alert"
          data-testid="mass-unlock-banner"
          className="rounded-md border border-destructive bg-destructive/10 px-3 py-2 font-medium text-destructive"
        >
          Every door on this floor is unlocked ({liveFloor.data.doors.length}). This usually means a fire-alarm release: check the fire
          panel and relock when it clears.
        </p>
      ) : null}
      {liveFloor.isError ? (
        <QueryErrorState error={liveFloor.error} message="Could not load the live floor." onRetry={() => liveFloor.refetch()} />
      ) : null}
      <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        {liveFloor.data !== undefined ? (
          <div className="flex flex-col gap-2">
            <div className="flex gap-2">
              <button type="button" className="rounded border px-2 py-1 text-sm" aria-pressed={replay} onClick={() => setReplay(!replay)}>
                {replay ? "Back to live" : "Replay"}
              </button>
            </div>
            {replay ? (
              <ReplayScrubber floor={liveFloor.data} />
            ) : (
              <LiveFloorPlan floor={liveFloor.data} onDoorSelect={canAct ? setSelectedDoor : undefined} />
            )}
          </div>
        ) : (
          <p>Loading floor…</p>
        )}
        <div className="flex flex-col gap-4">
          <AlarmPanel alarms={alarms.data ?? []} canAct={canAct} />
          <EventFeed events={liveFloor.data?.recentEvents ?? []} />
        </div>
      </div>
      <DoorActionsDialog door={selectedDoor} onClose={() => setSelectedDoor(null)} />
      {liveFloor.data !== undefined ? <LockdownDialog mode={lockdown} floor={liveFloor.data} onClose={() => setLockdown(null)} /> : null}
    </div>
  );
}

/** All doors of the floor (at least two) unlocked at once: a fire-alarm release in the PACS (lab finding #13). */
export function isMassUnlock(doors: readonly { state: string }[]): boolean {
  return doors.length >= 2 && doors.every((d) => d.state === "Unlocked" || d.state === "Open");
}
