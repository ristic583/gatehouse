import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AuditEntryDto, AuditVerificationDto } from "@/lib/api/types";
import { AuditPanel } from "./audit-panel";

const entry: AuditEntryDto = {
  sequence: 7,
  occurredAt: "2026-11-09T10:00:00Z",
  actorKind: "Person",
  actorId: "p1",
  actorName: "Jonas Berg",
  action: "fulfillment.confirmed",
  subjectType: "Fulfillment",
  subjectId: "f1",
  payload: {},
  hash: "abcdef0123456789abcdef",
};

const fetchMock = vi.fn<typeof fetch>();

function renderPanel(verification: AuditVerificationDto) {
  fetchMock.mockImplementation((input) => {
    const body = String(input).includes("/audit/verify") ? verification : [entry];
    return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } }));
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <AuditPanel />
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

describe("AuditPanel", () => {
  it("shows verified status", async () => {
    renderPanel({ verified: true, entryCount: 42, lastSequence: 42, firstInvalidSequence: null, verifiedAt: "2026-11-09T10:00:00Z" });
    fireEvent.click(screen.getByRole("button", { name: "Verify audit chain" }));
    expect(await screen.findByTestId("audit-status")).toHaveTextContent("Audit chain verified ✓ (42 entries)");
  });

  it("shows broken entry", async () => {
    renderPanel({ verified: false, entryCount: 42, lastSequence: 42, firstInvalidSequence: 9, verifiedAt: "2026-11-09T10:00:00Z" });
    fireEvent.click(screen.getByRole("button", { name: "Verify audit chain" }));
    expect(await screen.findByTestId("audit-status")).toHaveTextContent("Audit chain broken at entry #9 ✕");
  });

  it("lists recent entries", async () => {
    renderPanel({ verified: true, entryCount: 1, lastSequence: 7, firstInvalidSequence: null, verifiedAt: "2026-11-09T10:00:00Z" });
    expect(await screen.findByText("fulfillment.confirmed")).toBeInTheDocument();
    expect(screen.getByText("Jonas Berg")).toBeInTheDocument();
    expect(screen.getByText("abcdef012345")).toBeInTheDocument();
  });
});
