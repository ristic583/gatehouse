import { fireEvent, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AlarmDto } from "@/lib/api/types";
import { AcknowledgeAlarmDialog } from "./acknowledge-alarm-dialog";
import { jsonResponse, renderWithClient } from "./test-utils";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const alarm: AlarmDto = {
  alarmId: "a-1",
  doorId: "d-1",
  doorName: "Server door",
  areaId: "ar-1",
  areaName: "Server Room B",
  siteId: "s-1",
  kind: "Forced",
  raisedAt: "2026-11-10T10:00:00Z",
  doorEventId: "e-1",
  acknowledgedAt: null,
  acknowledgedByName: null,
  comment: null,
};

const fetchMock = vi.fn<typeof fetch>();
beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => vi.unstubAllGlobals());

describe("AcknowledgeAlarmDialog", () => {
  it("comment required", async () => {
    fetchMock.mockResolvedValue(jsonResponse({ ...alarm, acknowledgedAt: "2026-11-10T10:01:00Z", comment: "checked" }));
    const onClose = vi.fn();
    renderWithClient(<AcknowledgeAlarmDialog alarm={alarm} onClose={onClose} />);

    const submit = screen.getByRole("button", { name: "Acknowledge" });
    expect(submit).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Comment"), { target: { value: "   " } });
    expect(submit).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Comment"), { target: { value: "checked" } });
    expect(submit).toBeEnabled();
    fireEvent.click(submit);

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(String(url)).toContain("/v1/live/alarms/a-1/acknowledge");
    expect(init?.body).toBe(JSON.stringify({ comment: "checked" }));
  });
});
