import type { AreaAccessDto } from "@/lib/api/types";
export function roomAction(area: AreaAccessDto): { label: string; description: string; kind: "track" | "view" | "request" } {
  if (area.reason === "AreaNotMapped")
    return {
      label: "View access",
      kind: "view",
      description: "This space is not connected to an access system. Contact your administrator before requesting access.",
    };
  if (area.reason === "RevocationPending")
    return {
      label: "Track request",
      kind: "track",
      description: "Access removal is waiting for confirmation from the access system. Check the request for progress.",
    };
  if (area.state === "Pending")
    return {
      label: "Track request",
      kind: "track",
      description:
        area.reason === "AwaitingApproval"
          ? "Your request is waiting for the required approvals."
          : "Approval alone does not grant access. Wait for confirmation from the access system.",
    };
  if (area.state === "HasAccess")
    return {
      label: "View access",
      kind: "view",
      description: area.cardInactive
        ? "Access is assigned, but your card is inactive. Contact your security team."
        : "Your access is confirmed. Review its scope and access window.",
    };
  if (area.reason === "ScheduledStart" || area.reason === "OutsideWindow")
    return {
      label: "View access",
      kind: "view",
      description: "You have an access record outside its active window. Check its dates before making another request.",
    };
  return {
    label: "Request access",
    kind: "request",
    description:
      area.state === "Unknown"
        ? "Current access cannot be verified. A new request still requires approval and system confirmation."
        : "Choose when you need this space and explain your purpose.",
  };
}
