import type { StaffTaskDto } from "@/lib/api/types";

export interface ResolutionAction {
  value: string;
  label: string;
}

const RETRIED = { value: "Retried", label: "Retried" };
const CONFIRMED_MANUALLY = { value: "ConfirmedManually", label: "Confirmed manually" };
const VERIFIED_IN_PACS = { value: "VerifiedInPacs", label: "Verified in PACS" };
const DISMISSED = { value: "Dismissed", label: "Dismissed" };
const REMEDIATED = { value: "Remediated", label: "Re-push to PACS" };
const REMOVED_FROM_PACS = { value: "RemovedFromPacs", label: "Remove from PACS" };

const M3_ACTIONS: ResolutionAction[] = [RETRIED, CONFIRMED_MANUALLY, VERIFIED_IN_PACS, DISMISSED];

/** Kinds whose `Dismissed` resolution needs a note (domain-model.md §2.7 "Remediation"). */
const NOTE_REQUIRED_KINDS: ReadonlySet<string> = new Set(["ReconciliationFinding", "DriftAlert", "PinReveal"]);

/** The resolutions the API accepts for a task (authorization and remediation rules from M3 and M7). */
export function resolutionActionsFor(task: Pick<StaffTaskDto, "kind" | "findingKind">): ResolutionAction[] {
  if (task.kind === "ReconciliationFinding") {
    switch (task.findingKind) {
      case "MissingInPacs":
      case "Mismatch":
        return [REMEDIATED, VERIFIED_IN_PACS, DISMISSED];
      case "ExtraInPacs":
      case "ExpiredStillPresent":
        return [REMOVED_FROM_PACS, DISMISSED];
      default:
        return [DISMISSED];
    }
  }
  if (task.kind === "PinReveal") {
    return [CONFIRMED_MANUALLY, DISMISSED];
  }
  if (task.kind === "DriftAlert") {
    return [VERIFIED_IN_PACS, DISMISSED];
  }
  return M3_ACTIONS;
}

export function noteRequired(task: Pick<StaffTaskDto, "kind">, action: string): boolean {
  return action === "Dismissed" && NOTE_REQUIRED_KINDS.has(task.kind);
}
