import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AreaRosterDto, AreaRosterEntryDto } from "@/lib/api/types";
import { AreaRoster } from "./area-roster";

function entry(overrides: Partial<AreaRosterEntryDto>): AreaRosterEntryDto {
  return {
    entitlementId: "e1",
    subject: { personId: "a", displayName: "Anna Larsen" },
    personType: "Employee",
    requestedStart: "2026-11-10T00:00:00Z",
    effectiveEnd: "2026-12-10T00:00:00Z",
    status: "Approved",
    fulfillmentStatus: "Confirmed",
    revocationFulfillmentStatus: null,
    revokedAt: null,
    ...overrides,
  };
}

function roster(entries: AreaRosterEntryDto[], emergency: boolean): AreaRosterDto {
  return {
    area: { areaId: "x", name: "Server Room", siteName: "HQ", floorName: "Second floor" },
    canRevoke: true,
    canEmergencyRevoke: emergency,
    entries,
  };
}

const fetchMock = vi.fn<typeof fetch>();
function serve(body: AreaRosterDto) {
  fetchMock.mockResolvedValue(new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } }));
}
function show() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <AreaRoster areaId="x" />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => vi.unstubAllGlobals());

describe("AreaRoster", () => {
  it("owner sees revoke but not emergency revoke", async () => {
    serve(roster([entry({})], false));
    show();
    expect(await screen.findByRole("button", { name: "Revoke" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Emergency revoke" })).not.toBeInTheDocument();
    expect(screen.getByText("Server Room · HQ · Second floor")).toBeInTheDocument();
  });

  it("revoke requires a reason", async () => {
    serve(roster([entry({})], true));
    show();
    fireEvent.click(await screen.findByRole("button", { name: "Revoke" }));
    const submit = screen.getByRole("button", { name: "Revoke access" });
    expect(submit).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Reason"), { target: { value: "Left the project" } });
    expect(submit).toBeEnabled();
  });

  it("revocation pending row", async () => {
    serve(roster([entry({ status: "Revoked", revocationFulfillmentStatus: "Desired" })], false));
    show();
    expect(await screen.findByText("Revocation pending")).toBeInTheDocument();
  });
});
