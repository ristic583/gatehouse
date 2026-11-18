import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { FulfillmentDetailDto } from "@/lib/api/types";
import { FulfillmentDetail } from "./fulfillment-detail";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

function detail(overrides: Partial<FulfillmentDetailDto> = {}): FulfillmentDetailDto {
  return {
    item: {
      fulfillmentId: "f1",
      entitlementId: "e1",
      kind: "Grant",
      status: "Desired",
      priority: "Normal",
      revision: 2,
      subject: { personId: "p1", displayName: "Anna Larsen" },
      area: { areaId: "a1", name: "Server room", siteName: "HQ Copenhagen", floorName: "First floor" },
      pacsInstanceName: "Legacy PACS",
      vendorAccessLevelRef: "AL-7",
      entitlementStatus: "Approved",
      lastError: null,
      createdAt: "2026-11-09T10:00:00Z",
      updatedAt: "2026-11-09T10:00:00Z",
    },
    desired: {
      action: "Grant",
      personRef: "P-1",
      personName: "Anna Larsen",
      vendorAccessLevelRef: "AL-7",
      accessLevelDisplayName: "Server access",
      validFrom: "2026-11-10T00:00:00Z",
      validTo: "2026-12-10T00:00:00Z",
      pacsInstanceName: "Legacy PACS",
      vendor: "Acme",
    },
    confirmed: null,
    history: [],
    connectorManaged: false,
    commandStatus: null,
    commandAttempts: null,
    nextAttemptAt: null,
    lastError: null,
    ...overrides,
  };
}

const fetchMock = vi.fn<typeof fetch>();

function respond(body: FulfillmentDetailDto) {
  fetchMock.mockImplementation(() =>
    Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } })),
  );
}

function renderDetail() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <FulfillmentDetail id="f1" />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("FulfillmentDetail", () => {
  it("shows desired and confirmed state", async () => {
    const base = detail();
    respond(
      detail({
        item: { ...base.item, status: "Confirmed" },
        confirmed: {
          confirmedByKind: "Staff",
          confirmedById: "s1",
          confirmedByName: "Jonas Berg",
          confirmedAt: "2026-11-09T12:00:00Z",
          pacsRef: "PX-9",
        },
      }),
    );
    renderDetail();
    expect(await screen.findByText("Desired state")).toBeInTheDocument();
    expect(screen.getByText(/Confirmed by Jonas Berg \(Staff\)/)).toBeInTheDocument();
    expect(screen.getByText(/PX-9/)).toBeInTheDocument();
    expect(screen.queryByText("Desired and confirmed differ")).not.toBeInTheDocument();
  });

  it("unconfirmed shows not confirmed yet", async () => {
    respond(detail());
    renderDetail();
    expect(await screen.findByText("Not confirmed yet")).toBeInTheDocument();
    expect(screen.getByText("Desired and confirmed differ")).toBeInTheDocument();
  });

  it("fail requires a reason", async () => {
    respond(detail());
    renderDetail();
    fireEvent.click(await screen.findByRole("button", { name: "Fail" }));
    const confirm = await screen.findByRole("button", { name: "Confirm fail" });
    expect(confirm).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Reason"), { target: { value: "Controller offline" } });
    expect(confirm).toBeEnabled();
  });

  it("confirm posts to the confirm endpoint", async () => {
    respond(detail());
    renderDetail();
    fireEvent.click(await screen.findByRole("button", { name: "Confirm" }));
    await waitFor(() => {
      const call = fetchMock.mock.calls.find(([url]) => String(url).endsWith("/v1/fulfillments/f1/confirm"));
      expect(call?.[1]?.method).toBe("POST");
    });
  });

  it("hides manual actions when connector managed", async () => {
    respond(detail({ connectorManaged: true, commandStatus: "Dispatched", commandAttempts: 1 }));
    renderDetail();
    expect(await screen.findByTestId("connector-status")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Start" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Confirm" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Fail" })).not.toBeInTheDocument();
  });

  it("shows command status and attempts", async () => {
    const next = new Date(Date.now() + 5 * 60_000).toISOString();
    respond(
      detail({
        connectorManaged: true,
        commandStatus: "Retrying",
        commandAttempts: 3,
        nextAttemptAt: next,
        lastError: "Controller offline",
      }),
    );
    renderDetail();
    const panel = await screen.findByTestId("connector-status");
    expect(panel).toHaveTextContent("Handled by connector");
    expect(panel).toHaveTextContent("Command status: Retrying");
    expect(panel).toHaveTextContent("Attempts: 3");
    expect(panel).toHaveTextContent(/Next attempt in 5 minutes/);
    expect(panel).toHaveTextContent("Last error: Controller offline");
  });

  it("keeps retry for failed connector fulfillment", async () => {
    const base = detail();
    respond(detail({ item: { ...base.item, status: "Failed" }, connectorManaged: true, commandStatus: "Failed", commandAttempts: 5 }));
    renderDetail();
    expect(await screen.findByRole("button", { name: "Retry" })).toBeInTheDocument();
    expect(screen.getByTestId("connector-status")).toBeInTheDocument();
  });

  it("shows agent-enforced end date for grants", async () => {
    respond(
      detail({ connectorManaged: true, commandStatus: "Dispatched", commandAttempts: 1, capabilityGaps: ["NativeValidity", "Lockdown"] }),
    );
    renderDetail();
    const section = await screen.findByTestId("capability-gaps");
    expect(section).toHaveTextContent("No native end dates (agent removes access)");
    expect(section).toHaveTextContent("End date is enforced by the agent.");
  });
});
