import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { FulfillmentQueueItemDto } from "@/lib/api/types";
import { FulfillmentQueue } from "./fulfillment-queue";

function item(overrides: Partial<FulfillmentQueueItemDto>): FulfillmentQueueItemDto {
  return {
    fulfillmentId: "f1",
    entitlementId: "e1",
    kind: "Grant",
    status: "Desired",
    priority: "Normal",
    revision: 1,
    subject: { personId: "p1", displayName: "Anna Larsen" },
    area: { areaId: "a1", name: "Server room", siteName: "HQ Copenhagen", floorName: "First floor" },
    pacsInstanceName: "Legacy PACS",
    vendorAccessLevelRef: "AL-7",
    entitlementStatus: "Approved",
    lastError: null,
    createdAt: "2026-11-09T10:00:00Z",
    updatedAt: "2026-11-09T10:00:00Z",
    ...overrides,
  };
}

const fetchMock = vi.fn<typeof fetch>();

function respond(items: FulfillmentQueueItemDto[]) {
  fetchMock.mockImplementation(() =>
    Promise.resolve(new Response(JSON.stringify(items), { status: 200, headers: { "Content-Type": "application/json" } })),
  );
}

function renderQueue() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <FulfillmentQueue onSelect={() => undefined} />
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

describe("FulfillmentQueue", () => {
  it("lists open items with kind and status", async () => {
    respond([item({}), item({ fulfillmentId: "f2", kind: "Revoke", status: "Pending" })]);
    renderQueue();
    const row = await screen.findByTestId("fulfillment-f2");
    expect(row).toHaveTextContent("Revoke");
    expect(row).toHaveTextContent("Pending");
    expect(screen.getByTestId("fulfillment-f1")).toHaveTextContent("Grant");
  });

  it("high priority shows emergency badge", async () => {
    respond([item({ priority: "High", kind: "Revoke" })]);
    renderQueue();
    expect(await screen.findByText("Emergency")).toBeInTheDocument();
  });

  it("shows empty state", async () => {
    respond([]);
    renderQueue();
    expect(await screen.findByText("No fulfillment work waiting.")).toBeInTheDocument();
  });
});
