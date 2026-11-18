import type { LiveUpdateDto } from "@/lib/api/types";

/** `forbidden`: the server revoked the live membership (`SubscriptionRevoked`); terminal, no retries. */
export type LiveConnectionStatus = "connecting" | "connected" | "reconnecting" | "disconnected" | "forbidden";

/** The slice of a SignalR `HubConnection` the live hook uses (so tests can pass a fake). */
export interface LiveConnection {
  on(methodName: "live", handler: (update: LiveUpdateDto) => void): void;
  on(methodName: "SubscriptionRevoked", handler: (siteId: string, code: string) => void): void;
  onreconnecting(callback: () => void): void;
  onreconnected(callback: () => void): void;
  onclose(callback: () => void): void;
  start(): Promise<void>;
  stop(): Promise<void>;
  invoke(methodName: "JoinSite", siteId: string): Promise<unknown>;
}

export type LiveConnectionFactory = () => LiveConnection;
