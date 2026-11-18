import { fireEvent, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { LiveDoorDto } from "@/lib/api/types";
import { DoorActionsDialog, isValidDuration } from "./door-actions-dialog";
import { renderWithClient } from "./test-utils";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const door: LiveDoorDto = {
  doorId: "d-1",
  areaId: "ar-1",
  name: "Loading dock",
  position: { x: 0, y: 0 },
  pacsDoorRef: "D-DOCK",
  connectorManaged: true,
  state: "Locked",
  stateSince: null,
  readers: [],
  canUnlock: true,
  canLockdown: true,
};

describe("DoorActionsDialog", () => {
  it("duration bounds", () => {
    expect(isValidDuration("0")).toBe(false);
    expect(isValidDuration("1")).toBe(true);
    expect(isValidDuration("60")).toBe(true);
    expect(isValidDuration("61")).toBe(false);
    expect(isValidDuration("2.5")).toBe(false);
    expect(isValidDuration("")).toBe(false);

    renderWithClient(<DoorActionsDialog door={door} onClose={vi.fn()} />);
    const submit = screen.getByRole("button", { name: "Momentary unlock" });
    fireEvent.change(screen.getByLabelText("Reason"), { target: { value: "Delivery" } });
    expect(submit).toBeEnabled();

    fireEvent.change(screen.getByLabelText(/Duration/), { target: { value: "61" } });
    expect(submit).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/Duration/), { target: { value: "0" } });
    expect(submit).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/Duration/), { target: { value: "60" } });
    expect(submit).toBeEnabled();
  });

  it("hides unlock when unsupported", () => {
    renderWithClient(<DoorActionsDialog door={{ ...door, canUnlock: false }} onClose={vi.fn()} />);
    expect(screen.getByTestId("unlock-unsupported")).toHaveTextContent("This door's PACS does not support remote unlock.");
    expect(screen.queryByRole("button", { name: "Momentary unlock" })).not.toBeInTheDocument();
  });
});
