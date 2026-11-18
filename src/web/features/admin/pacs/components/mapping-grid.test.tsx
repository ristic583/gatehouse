import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { AccessLevelMappingRequest } from "@/lib/api/types";
import { MappingGrid } from "./mapping-grid";

const areas = [
  { areaId: "a1", name: "Lab", siteName: "HQ", floorName: "Ground" },
  { areaId: "a2", name: "Server room", siteName: "HQ", floorName: "Ground" },
  { areaId: "a3", name: "Lobby", siteName: "HQ", floorName: "Ground" },
];

describe("MappingGrid", () => {
  it("shows unmapped areas", async () => {
    const onSave = vi.fn<(b: AccessLevelMappingRequest[]) => Promise<unknown>>(() => Promise.resolve());
    render(
      <MappingGrid
        areas={areas}
        mappings={[{ areaId: "a1", areaName: "Lab", vendorAccessLevelRef: "AL-LAB", displayName: "Lab" }]}
        onSave={onSave}
      />,
    );

    expect(screen.getAllByTestId("mapping-row")).toHaveLength(3);
    expect(screen.getByTestId("unmapped-count")).toHaveTextContent("2 unmapped");
    expect(screen.getByLabelText("Access level for Server room")).toHaveValue("");

    fireEvent.change(screen.getByLabelText("Access level for Server room"), { target: { value: "AL-SRV" } });
    expect(screen.getByTestId("unmapped-count")).toHaveTextContent("1 unmapped");
    fireEvent.click(screen.getByRole("button", { name: "Save mappings" }));
    await waitFor(() =>
      expect(onSave).toHaveBeenCalledWith([
        { areaId: "a1", vendorAccessLevelRef: "AL-LAB", displayName: "Lab" },
        { areaId: "a2", vendorAccessLevelRef: "AL-SRV", displayName: null },
      ]),
    );
  });
});
