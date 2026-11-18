import type { CapabilityGap } from "@/lib/api/types";

/** What a connector cannot do (adapter-interface.md §6), as staff-facing text. */
export const CAPABILITY_GAP_LABELS: Record<CapabilityGap, string> = {
  NativeValidity: "No native end dates (agent removes access)",
  PinWrite: "No PIN write (manual PIN reveal)",
  PhotoWrite: "No photo write",
  RecordExport: "No record export",
  MomentaryUnlock: "No remote unlock",
  Lockdown: "No lockdown",
};

export function capabilityGapLabel(gap: CapabilityGap): string {
  return CAPABILITY_GAP_LABELS[gap];
}
