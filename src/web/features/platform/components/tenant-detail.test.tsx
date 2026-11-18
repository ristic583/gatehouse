import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { TenantDetailView } from "./tenant-detail";
import { platformTenant } from "./test-data";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

function actions() {
  return { onSuspend: vi.fn(), onReactivate: vi.fn(), onRetry: vi.fn(), onResendInvite: vi.fn() };
}

describe("TenantDetailView", () => {
  it("suspend requires a reason", () => {
    const handlers = actions();
    render(<TenantDetailView tenant={platformTenant()} {...handlers} />);

    fireEvent.click(screen.getByRole("button", { name: "Suspend…" }));
    const dialog = screen.getByRole("dialog");
    const confirm = screen.getByRole("button", { name: "Suspend" });
    expect(dialog).toBeInTheDocument();
    expect(confirm).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Reason"), { target: { value: "   " } });
    fireEvent.blur(screen.getByLabelText("Reason"));
    expect(confirm).toBeDisabled();
    expect(screen.getByText(/A reason is required/)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Reason"), { target: { value: " Unpaid invoice " } });
    fireEvent.click(confirm);
    expect(handlers.onSuspend).toHaveBeenCalledWith("Unpaid invoice");
  });

  it("retry shown only for failed provisioning", () => {
    const { rerender } = render(<TenantDetailView tenant={platformTenant()} {...actions()} />);
    expect(screen.queryByRole("button", { name: "Retry provisioning" })).not.toBeInTheDocument();

    rerender(
      <TenantDetailView
        tenant={platformTenant({
          status: "Provisioning",
          provisioning: {
            step: "Baseline",
            state: "Running",
            attempts: 1,
            lastError: null,
            startedAt: "2026-11-14T10:00:00Z",
            completedAt: null,
            adminInvitedAt: null,
            adminActivatedAt: null,
          },
        })}
        {...actions()}
      />,
    );
    expect(screen.queryByRole("button", { name: "Retry provisioning" })).not.toBeInTheDocument();

    const handlers = actions();
    rerender(
      <TenantDetailView
        tenant={platformTenant({
          status: "ProvisioningFailed",
          provisioning: {
            step: "Organization",
            state: "Failed",
            attempts: 3,
            lastError: "keycloak.unavailable: down",
            startedAt: "2026-11-14T10:00:00Z",
            completedAt: null,
            adminInvitedAt: null,
            adminActivatedAt: null,
          },
        })}
        {...handlers}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Retry provisioning" }));
    expect(handlers.onRetry).toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Suspend…" })).not.toBeInTheDocument();
  });
});
