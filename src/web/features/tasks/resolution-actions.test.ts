import { describe, expect, it } from "vitest";
import { noteRequired, resolutionActionsFor } from "./resolution-actions";

const values = (kind: string, findingKind?: string | null) => resolutionActionsFor({ kind, findingKind }).map((a) => a.value);

describe("resolutionActionsFor", () => {
  it("keeps the M3 actions for fulfillment tasks", () => {
    expect(values("FulfillmentFailed")).toEqual(["Retried", "ConfirmedManually", "VerifiedInPacs", "Dismissed"]);
  });

  it("maps finding kinds to their remediation actions", () => {
    expect(values("ReconciliationFinding", "MissingInPacs")).toEqual(["Remediated", "VerifiedInPacs", "Dismissed"]);
    expect(values("ReconciliationFinding", "Mismatch")).toEqual(["Remediated", "VerifiedInPacs", "Dismissed"]);
    expect(values("ReconciliationFinding", "ExtraInPacs")).toEqual(["RemovedFromPacs", "Dismissed"]);
    expect(values("ReconciliationFinding", "ExpiredStillPresent")).toEqual(["RemovedFromPacs", "Dismissed"]);
    expect(values("DriftAlert")).toEqual(["VerifiedInPacs", "Dismissed"]);
  });

  it("needs a note only to dismiss M7 tasks", () => {
    expect(noteRequired({ kind: "DriftAlert" }, "Dismissed")).toBe(true);
    expect(noteRequired({ kind: "DriftAlert" }, "VerifiedInPacs")).toBe(false);
    expect(noteRequired({ kind: "FulfillmentFailed" }, "Dismissed")).toBe(false);
  });
});
