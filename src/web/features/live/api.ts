import { apiClient } from "@/lib/api/client";
import type {
  AcknowledgeAlarmDto,
  AlarmDto,
  ConnectorHealthDto,
  DoorCommandResultDto,
  LiveDoorEventDto,
  LiveFloorDto,
  RequestDoorCommandDto,
} from "@/lib/api/types";

export type AlarmStatusFilter = "open" | "acknowledged";

/** TanStack Query keys for live data; `useLiveSite` patches these caches from SignalR updates. */
export const liveKeys = {
  floor: (floorId: string) => ["live-floor", floorId] as const,
  floors: ["live-floor"] as const,
  connectors: (siteId?: string) => (siteId === undefined ? (["live-connectors"] as const) : (["live-connectors", siteId] as const)),
  alarms: (status?: AlarmStatusFilter, siteId?: string) =>
    status === undefined ? (["live-alarms"] as const) : (["live-alarms", status, siteId ?? null] as const),
};

function query(params: Record<string, string | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined) {
      search.set(key, value);
    }
  }
  const text = search.toString();
  return text === "" ? "" : `?${text}`;
}

export const liveApi = {
  floor: (floorId: string) => apiClient.get<LiveFloorDto>(`/v1/live/floors/${floorId}`),
  floorEvents: (floorId: string, from: string, to: string) =>
    apiClient.get<LiveDoorEventDto[]>(`/v1/live/floors/${floorId}/events${query({ from, to })}`),
  connectors: (siteId?: string) => apiClient.get<ConnectorHealthDto[]>(`/v1/live/connectors${query({ siteId })}`),
  alarms: (status: AlarmStatusFilter = "open", siteId?: string) => apiClient.get<AlarmDto[]>(`/v1/live/alarms${query({ status, siteId })}`),
  acknowledgeAlarm: (alarmId: string, body: AcknowledgeAlarmDto) =>
    apiClient.post<AlarmDto>(`/v1/live/alarms/${alarmId}/acknowledge`, body),
  doorCommand: (body: RequestDoorCommandDto) => apiClient.post<DoorCommandResultDto>("/v1/live/door-commands", body),
};

/** BFF path of the SignalR hub (`/api/v1/live/hub` upstream). */
export const LIVE_HUB_URL = "/api/proxy/v1/live/hub";
