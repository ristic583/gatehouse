import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PeopleList } from "@/features/people/components/people-list";
import { MyRequestsTableView } from "@/features/requests/components/my-requests-table";
import type { AccessRequestDto, ApprovalInboxItemDto, PersonSummaryDto } from "@/lib/api/types";
import { ApprovalCard } from "./approval-card";

/**
 * Security regression (M11, area 4): user-supplied names, area/site names and justifications are rendered as text. Markup stays
 * visible as characters and never becomes an element, an event handler or a link.
 */
const IMG = `<img src=x onerror="window.__xss=1">`;
const SCRIPT = `<script>window.__xss=1</script>`;
const LINK = `<a href="javascript:window.__xss=1">click</a>`;

function expectInert(container: HTMLElement) {
  expect(container.querySelector("img[onerror], script, a[href^='javascript']")).toBeNull();
  expect((window as unknown as { __xss?: number }).__xss).toBeUndefined();
}

const inboxItem: ApprovalInboxItemDto = {
  entitlementId: "e1",
  stepId: "s1",
  stepOrder: 1,
  approverKind: "AreaOwner",
  subject: { personId: "a", displayName: `Eve ${IMG}` },
  requestedBy: { personId: "b", displayName: `Mallory ${SCRIPT}` },
  area: { areaId: "x", name: `Lab ${LINK}`, siteName: `HQ ${IMG}`, floorName: `First ${SCRIPT}` },
  securityLevelName: "Medium",
  requestedStart: "2026-11-10T00:00:00Z",
  requestedEnd: "2026-12-10T00:00:00Z",
  effectiveEnd: "2026-12-10T00:00:00Z",
  cappedToEligibility: false,
  justification: `Because ${SCRIPT}${IMG}`,
  createdAt: "2026-11-09T00:00:00Z",
};

afterEach(() => vi.unstubAllGlobals());

describe("markup in user-supplied text", () => {
  it("approval card shows names, area and justification as text", () => {
    const { container } = render(<ApprovalCard item={inboxItem} disabled={false} onReject={vi.fn()} onApprove={vi.fn()} />);

    expect(container).toHaveTextContent(`Because ${SCRIPT}${IMG}`);
    expect(container).toHaveTextContent(`Eve ${IMG}`);
    expect(container).toHaveTextContent(`Lab ${LINK}`);
    expectInert(container);
  });

  it("request list shows area, site and justification as text", () => {
    const request: AccessRequestDto = {
      entitlementId: "e1",
      subject: inboxItem.subject,
      requestedBy: inboxItem.requestedBy,
      area: inboxItem.area,
      requestedStart: inboxItem.requestedStart,
      requestedEnd: inboxItem.requestedEnd,
      effectiveEnd: inboxItem.effectiveEnd,
      cappedToEligibility: false,
      justification: inboxItem.justification,
      status: "PendingApproval",
      steps: [],
      fulfillmentStatus: null,
      createdAt: inboxItem.createdAt,
      revocation: null,
      revocationFulfillmentStatus: null,
    };

    const { container } = render(<MyRequestsTableView requests={[request]} />);

    expect(container).toHaveTextContent(`Lab ${LINK}`);
    expectInert(container);
  });

  it("people list shows names and emails as text", async () => {
    const person: PersonSummaryDto = {
      personId: "p-1",
      displayName: `Eve ${IMG}`,
      email: `eve+${SCRIPT}@nordlys.test`,
      type: "External",
      source: "Local",
      status: "Active",
      eligibilityStart: "2026-11-01T00:00:00Z",
      eligibilityEnd: "2026-12-01T00:00:00Z",
      sponsor: { personId: "p-9", displayName: `Sponsor ${LINK}` },
      manager: null,
    };
    vi.stubGlobal(
      "fetch",
      vi.fn<typeof fetch>(() =>
        Promise.resolve(new Response(JSON.stringify([person]), { headers: { "Content-Type": "application/json" } })),
      ),
    );
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    const { container } = render(
      <QueryClientProvider client={client}>
        <PeopleList selectedId={null} onSelect={vi.fn()} />
      </QueryClientProvider>,
    );

    expect(await screen.findByText(`Eve ${IMG}`)).toBeInTheDocument();
    expectInert(container);
  });
});
