import { fireEvent, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { LiveFloorDto } from "@/lib/api/types";
import { LockdownDialog } from "./lockdown-dialog";
import { jsonResponse, renderWithClient } from "./test-utils";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const floor: LiveFloorDto = {
  floorId: "f-1",
  siteId: "s-1",
  siteName: "HQ",
  floorName: "Ground",
  planWidth: 100,
  planHeight: 100,
  areas: [{ areaId: "ar-1", name: "Lab", code: "LAB", polygon: [] }],
  doors: [],
  openAlarms: [],
  recentEvents: [],
};

const fetchMock = vi.fn<typeof fetch>();
beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => vi.unstubAllGlobals());

describe("LockdownDialog", () => {
  it("requires reason and confirmation", async () => {
    fetchMock.mockResolvedValue(jsonResponse({ doorCommandId: "c-1", doorCount: 3, skippedDoorCount: 0 }));
    const onClose = vi.fn();
    renderWithClient(<LockdownDialog mode="start" floor={floor} onClose={onClose} />);

    const next = screen.getByRole("button", { name: "Continue" });
    expect(next).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Reason"), { target: { value: "Intruder" } });
    expect(next).toBeEnabled();

    fireEvent.click(next);
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent("Lock down floor Ground?");

    fireEvent.click(screen.getByRole("button", { name: "Confirm lockdown" }));
    expect(await screen.findByTestId("lockdown-result")).toHaveTextContent("Sent to 3 doors");
    expect(onClose).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Done" }));
    expect(onClose).toHaveBeenCalled();
    const [, init] = fetchMock.mock.calls[0] ?? [];
    expect(JSON.parse(String(init?.body))).toEqual({
      kind: "LockdownStart",
      scope: "Floor",
      scopeId: "f-1",
      reason: "Intruder",
      durationSeconds: null,
    });
  });

  function confirmLockdown() {
    fireEvent.change(screen.getByLabelText("Reason"), { target: { value: "Drill" } });
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));
    fireEvent.click(screen.getByRole("button", { name: "Confirm lockdown" }));
  }

  it("shows skipped count", async () => {
    fetchMock.mockResolvedValue(jsonResponse({ doorCommandId: "c-1", doorCount: 2, skippedDoorCount: 3 }));
    renderWithClient(<LockdownDialog mode="start" floor={floor} onClose={vi.fn()} />);
    confirmLockdown();
    expect(await screen.findByTestId("lockdown-result")).toHaveTextContent(
      "Sent to 2 doors, 3 skipped (not connector-managed or no lockdown support)",
    );
  });

  it("shows unsupported message on 409", async () => {
    fetchMock.mockResolvedValue(
      jsonResponse({ title: "Conflict", status: 409, code: "door_command.unsupported_by_pacs", detail: "Lockdown unsupported." }, 409),
    );
    const onClose = vi.fn();
    renderWithClient(<LockdownDialog mode="start" floor={floor} onClose={onClose} />);
    confirmLockdown();
    expect(await screen.findByTestId("lockdown-unsupported")).toHaveTextContent("Lockdown is not supported by the PACS in this scope.");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });
});
