import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { AccessRequestDto, ApprovalStepDto } from "@/lib/api/types";
import { MyRequestsTableView, statusText } from "./my-requests-table";

function request(overrides: Partial<AccessRequestDto>): AccessRequestDto {
  return {
    entitlementId: "e1",
    subject: { personId: "a", displayName: "Anna Larsen" },
    requestedBy: { personId: "a", displayName: "Anna Larsen" },
    area: { areaId: "x", name: "Server Room", siteName: "HQ Copenhagen", floorName: "Second floor" },
    requestedStart: "2026-11-10T00:00:00Z",
    requestedEnd: "2026-12-10T00:00:00Z",
    effectiveEnd: "2026-12-10T00:00:00Z",
    cappedToEligibility: false,
    justification: "Needed for work",
    status: "PendingApproval",
    steps: [],
    fulfillmentStatus: null,
    createdAt: "2026-11-09T00:00:00Z",
    revocation: null,
    revocationFulfillmentStatus: null,
    ...overrides,
  };
}

describe("MyRequestsTable", () => {
  it("shows capped badge", () => {
    render(<MyRequestsTableView requests={[request({ cappedToEligibility: true })]} />);
    expect(screen.getByText("capped to eligibility")).toBeInTheDocument();
  });

  it("approved but unconfirmed is not shown as active", () => {
    render(<MyRequestsTableView requests={[request({ status: "Approved", fulfillmentStatus: "Desired" })]} />);
    expect(screen.getByText("Approved - not yet applied")).toBeInTheDocument();
    expect(screen.queryByText("Active")).not.toBeInTheDocument();
  });

  it("pending request whose window ended shows expired before a decision", () => {
    const closed: ApprovalStepDto = {
      stepId: "s1",
      order: 1,
      approverKind: "AreaOwner",
      status: "Closed",
      assignedApprover: null,
      decidedBy: null,
      decidedAt: null,
      comment: null,
    };
    expect(statusText(request({ status: "Expired", steps: [closed] }))).toBe("Expired before a decision");
    expect(statusText(request({ status: "Expired", steps: [{ ...closed, status: "Approved" }] }))).toBe("Expired");
  });

  it("revoked with pending removal", () => {
    render(<MyRequestsTableView requests={[request({ status: "Revoked", revocationFulfillmentStatus: "Desired" })]} />);
    expect(screen.getByText("Revoked - removal pending")).toBeInTheDocument();
  });

  it("cancel shown only for own pending requests", () => {
    render(
      <MyRequestsTableView
        currentPersonId="a"
        requests={[
          request({ entitlementId: "own" }),
          request({ entitlementId: "other", requestedBy: { personId: "b", displayName: "B" } }),
          request({ entitlementId: "done", status: "Approved" }),
        ]}
      />,
    );
    expect(screen.getAllByRole("button", { name: "Cancel request" })).toHaveLength(1);
    expect(within(screen.getByTestId("request-own")).getByRole("button", { name: "Cancel request" })).toBeInTheDocument();
  });
});

it("does not describe confirmed future or ended windows as active", () => {
  const confirmed = request({ status: "Approved", fulfillmentStatus: "Confirmed" });
  expect(statusText(confirmed, new Date("2026-11-09T00:00:00Z"))).toBe("Scheduled");
  expect(statusText(confirmed, new Date("2026-11-15T00:00:00Z"))).toBe("Active");
  expect(statusText(confirmed, new Date("2026-12-10T00:00:00Z"))).toBe("Window ended");
});
