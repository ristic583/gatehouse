import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { AssignRoleRequest, PersonSummaryDto, SiteDto } from "@/lib/api/types";
import { AssignRoleDialog } from "./role-assignments";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const sites: SiteDto[] = [{ siteId: "s1", name: "HQ", code: "HQ", timeZoneId: "Europe/Copenhagen", floors: [] }];
const ada: PersonSummaryDto = {
  personId: "p1",
  displayName: "Ada Admin",
  email: "ada@acme.test",
  type: "Employee",
  source: "Authoritative",
  status: "Active",
  eligibilityStart: "2026-01-01T00:00:00Z",
  eligibilityEnd: null,
  sponsor: null,
  manager: null,
};

describe("AssignRoleDialog", () => {
  it("site scope only for security staff", async () => {
    const onAssign = vi.fn<(r: AssignRoleRequest) => Promise<unknown>>(() => Promise.resolve());
    render(<AssignRoleDialog sites={sites} searchPeople={() => Promise.resolve([ada])} onAssign={onAssign} onClose={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: "Search" }));
    fireEvent.click(await screen.findByRole("button", { name: /Ada Admin/ }));
    expect(screen.queryByLabelText("Site (optional)")).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Role"), { target: { value: "SecurityStaff" } });
    fireEvent.change(screen.getByLabelText("Site (optional)"), { target: { value: "s1" } });
    fireEvent.change(screen.getByLabelText("Role"), { target: { value: "TenantAdmin" } });
    expect(screen.queryByLabelText("Site (optional)")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Assign" }));
    await waitFor(() => expect(onAssign).toHaveBeenCalledWith({ personId: "p1", role: "TenantAdmin", siteId: null }));

    fireEvent.change(screen.getByLabelText("Role"), { target: { value: "SecurityStaff" } });
    fireEvent.click(screen.getByRole("button", { name: "Assign" }));
    await waitFor(() => expect(onAssign).toHaveBeenLastCalledWith({ personId: "p1", role: "SecurityStaff", siteId: "s1" }));
  });
});
