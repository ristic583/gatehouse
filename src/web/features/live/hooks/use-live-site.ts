"use client";

import { HttpTransportType, HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import type { AlarmDto, ConnectorHealthDto, LiveFloorDto, LiveUpdateDto } from "@/lib/api/types";
import { LIVE_HUB_URL, liveKeys } from "../api";
import type { LiveConnection, LiveConnectionFactory, LiveConnectionStatus } from "../types";

/** Waits before retrying a failed initial start (SignalR's automatic reconnect only covers established connections: GH-WEB-02). */
export const INITIAL_START_RETRY_MS: readonly number[] = [2_000, 5_000, 10_000, 30_000];

/** Maximum events kept in a floor's recent feed after live prepends. */
export const RECENT_EVENT_LIMIT = 50;

export function createLiveConnection(): LiveConnection {
  return new HubConnectionBuilder()
    .withUrl(LIVE_HUB_URL, { transport: HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}

function upsertAlarm(list: AlarmDto[], alarm: AlarmDto, keepAcknowledged: boolean): AlarmDto[] {
  const without = list.filter((a) => a.alarmId !== alarm.alarmId);
  if (alarm.acknowledgedAt !== null && !keepAcknowledged) {
    return without;
  }
  return [alarm, ...without];
}

function applyToFloors(client: QueryClient, update: LiveUpdateDto): void {
  client.setQueriesData<LiveFloorDto>({ queryKey: liveKeys.floors }, (floor) => {
    if (floor === undefined || floor.siteId !== update.siteId) {
      return floor;
    }
    const { doorState, doorEvent, alarm } = update;
    const occupancy = update.occupancy ?? null;
    if (occupancy !== null) {
      if (!floor.areas.some((a) => a.areaId === occupancy.areaId)) {
        return floor;
      }
      const others = (floor.occupancy ?? []).filter((o) => o.areaId !== occupancy.areaId);
      return { ...floor, occupancy: [...others, occupancy] };
    }
    if (doorState !== null) {
      if (!floor.doors.some((d) => d.doorId === doorState.doorId)) {
        return floor;
      }
      return {
        ...floor,
        doors: floor.doors.map((d) => (d.doorId === doorState.doorId ? { ...d, state: doorState.state, stateSince: doorState.since } : d)),
      };
    }
    if (doorEvent !== null) {
      if (doorEvent.doorId === null || !floor.doors.some((d) => d.doorId === doorEvent.doorId)) {
        return floor;
      }
      if (floor.recentEvents.some((e) => e.eventId === doorEvent.eventId)) {
        return floor;
      }
      return { ...floor, recentEvents: [doorEvent, ...floor.recentEvents].slice(0, RECENT_EVENT_LIMIT) };
    }
    if (alarm !== null) {
      if (!floor.doors.some((d) => d.doorId === alarm.doorId)) {
        return floor;
      }
      return { ...floor, openAlarms: upsertAlarm(floor.openAlarms, alarm, false) };
    }
    return floor;
  });
}

function applyToAlarms(client: QueryClient, alarm: AlarmDto): void {
  for (const query of client.getQueryCache().findAll({ queryKey: ["live-alarms"] })) {
    const [, status, siteId] = query.queryKey as readonly [string, string?, (string | null)?];
    if (siteId !== undefined && siteId !== null && siteId !== alarm.siteId) {
      continue;
    }
    const acknowledged = alarm.acknowledgedAt !== null;
    client.setQueryData<AlarmDto[]>(query.queryKey, (list) => {
      if (list === undefined) {
        return list;
      }
      if (status === "acknowledged") {
        return acknowledged ? upsertAlarm(list, alarm, true) : list.filter((a) => a.alarmId !== alarm.alarmId);
      }
      return upsertAlarm(list, alarm, false);
    });
  }
}

function applyToConnectors(client: QueryClient, connector: ConnectorHealthDto): void {
  client.setQueriesData<ConnectorHealthDto[]>({ queryKey: liveKeys.connectors() }, (list) =>
    list?.map((c) => (c.pacsInstanceId === connector.pacsInstanceId ? connector : c)),
  );
}

/** Applies one SignalR `live` message to the TanStack Query caches. */
export function applyLiveUpdate(client: QueryClient, update: LiveUpdateDto): void {
  switch (update.kind) {
    case "DoorState":
    case "DoorEvent":
    case "Occupancy":
      applyToFloors(client, update);
      break;
    case "Alarm":
      if (update.alarm !== null) {
        applyToFloors(client, update);
        applyToAlarms(client, update.alarm);
      }
      break;
    case "ConnectorHealth":
      if (update.connector !== null) {
        applyToConnectors(client, update.connector);
      }
      break;
  }
}

export interface UseLiveSiteOptions {
  /** Test seam: defaults to a real SignalR connection to the BFF hub. */
  connectionFactory?: LiveConnectionFactory;
}

/** Subscribes to live updates for a site and keeps the live query caches current. */
export function useLiveSite(siteId: string | null, options: UseLiveSiteOptions = {}): LiveConnectionStatus {
  const client = useQueryClient();
  const factory = options.connectionFactory ?? createLiveConnection;
  const [status, setStatus] = useState<LiveConnectionStatus>(siteId === null ? "disconnected" : "connecting");

  useEffect(() => {
    if (siteId === null) {
      return;
    }
    let disposed = false;
    // Set once the server revoked the membership (GH-AUTH-02): show "not permitted" and never rejoin or retry.
    let revoked = false;
    const connection = factory();
    const join = async () => {
      await connection.invoke("JoinSite", siteId);
    };

    connection.on("live", (update) => {
      applyLiveUpdate(client, update);
    });
    connection.on("SubscriptionRevoked", (revokedSiteId) => {
      if (disposed || revokedSiteId !== siteId) return;
      revoked = true;
      setStatus("forbidden");
      void connection.stop();
    });
    connection.onreconnecting(() => {
      if (!disposed && !revoked) setStatus("reconnecting");
    });
    connection.onreconnected(() => {
      if (revoked) return;
      void join()
        .then(() => {
          if (disposed) return;
          setStatus("connected");
          // Anything that happened while disconnected is only in the API: refetch.
          void client.invalidateQueries({ queryKey: liveKeys.floors });
          void client.invalidateQueries({ queryKey: liveKeys.alarms() });
          void client.invalidateQueries({ queryKey: liveKeys.connectors() });
        })
        .catch(() => {
          if (!disposed) setStatus("disconnected");
        });
    });
    connection.onclose(() => {
      if (!disposed && !revoked) setStatus("disconnected");
    });

    let retryTimer: ReturnType<typeof setTimeout> | undefined;
    let attempt = 0;
    const startOnce = () => {
      connection.start().then(
        () => {
          void join()
            .then(() => {
              if (!disposed) setStatus("connected");
            })
            .catch(() => {
              if (!disposed) setStatus("disconnected");
            });
        },
        () => {
          if (disposed || revoked) return;
          setStatus("disconnected");
          const delay = INITIAL_START_RETRY_MS[Math.min(attempt, INITIAL_START_RETRY_MS.length - 1)] ?? 30_000;
          attempt++;
          retryTimer = setTimeout(() => {
            if (disposed || revoked) return;
            setStatus("connecting");
            startOnce();
          }, delay);
        },
      );
    };
    startOnce();

    return () => {
      disposed = true;
      if (retryTimer !== undefined) clearTimeout(retryTimer);
      void connection.stop();
    };
  }, [client, factory, siteId]);

  return status;
}
