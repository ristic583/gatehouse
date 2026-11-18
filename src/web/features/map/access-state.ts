import { Check, CircleHelp, Hourglass, X, type LucideIcon } from "lucide-react";
import type { AreaAccessReason, AreaAccessState } from "@/lib/api/types";

export interface AccessStateMeta {
  label: string;
  icon: LucideIcon;
  colorVar: string;
  /** Fixed canvas equivalent of colorVar; never a tenant theme color. */
  colorHex: string;
  hatched: boolean;
}

/** Fixed state semantics (ADR-007): never derived from theme tokens. */
export const ACCESS_STATE_META: Record<AreaAccessState, AccessStateMeta> = {
  HasAccess: { label: "Has access", icon: Check, colorVar: "--gh-state-has-access", colorHex: "#15803d", hatched: false },
  NoAccess: { label: "No access", icon: X, colorVar: "--gh-state-no-access", colorHex: "#b91c1c", hatched: false },
  Pending: { label: "Pending", icon: Hourglass, colorVar: "--gh-state-pending", colorHex: "#b45309", hatched: true },
  Unknown: { label: "Unknown", icon: CircleHelp, colorVar: "--gh-state-unknown", colorHex: "#6b7280", hatched: false },
};

export const ACCESS_STATES: readonly AreaAccessState[] = ["HasAccess", "NoAccess", "Pending", "Unknown"];

export const CARD_INACTIVE_NOTE = "assigned access; card inactive";

function formatTime(iso: string): string {
  const date = new Date(iso);
  return `${String(date.getHours()).padStart(2, "0")}:${String(date.getMinutes()).padStart(2, "0")}`;
}

export function reasonText(reason: AreaAccessReason, since: string | null): string {
  switch (reason) {
    case "AreaNotMapped":
      return "not connected to a PACS";
    case "ConnectorOffline":
      return since === null ? "connector offline" : `connector offline since ${formatTime(since)}`;
    case "Confirmed":
      return "access confirmed";
    case "RevocationPending":
      return "revocation not yet applied";
    case "AwaitingApproval":
      return "awaiting approval";
    case "AwaitingFulfillment":
      return "approved, not yet applied";
    case "ScheduledStart":
      return "starts later";
    case "OutsideWindow":
      return "outside the access window";
    case "NoEntitlement":
      return "no access requested";
  }
}
