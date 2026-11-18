const BLOCKED_REASON_LABELS: Record<string, string> = {
  "reconciliation.connector_required": "Only connector-managed PACS instances can be reconciled.",
  "reconciliation.export_unsupported": "This PACS cannot export its records.",
  "reconciliation.already_running": "A reconciliation of this PACS instance is already running.",
  "reconciliation.instance_not_found": "This PACS instance no longer exists.",
  "auth.forbidden": "You need security staff rights for every site of this PACS instance.",
};

/** Text for a `BlockedReason` or a run error code. */
export function blockedReasonLabel(code: string | null | undefined): string {
  return (code === null || code === undefined ? undefined : BLOCKED_REASON_LABELS[code]) ?? "The reconciliation could not be started.";
}

export const REPORT_STATUS_LABELS: Record<string, string> = {
  Requested: "Requested",
  Exporting: "Exporting",
  Comparing: "Comparing",
  Completed: "Completed",
  Failed: "Failed",
};

export const FINDING_KIND_LABELS: Record<string, string> = {
  MissingInPacs: "Missing in PACS",
  ExtraInPacs: "Extra in PACS",
  Mismatch: "Mismatch",
  ExpiredStillPresent: "Expired still present",
};

export function reportStatusLabel(status: string): string {
  return REPORT_STATUS_LABELS[status] ?? status;
}
