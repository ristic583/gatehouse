import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AppHeader } from "./app-header";

describe("AppHeader", () => {
  it("hides approvals link for plain employees", () => {
    render(<AppHeader tenantName="Nordlys A/S" displayName="Anna Larsen" roles={["Employee"]} />);
    expect(screen.getByRole("link", { name: "Map" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "My requests" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Approvals" })).not.toBeInTheDocument();
  });

  it("shows approvals link for managers", () => {
    render(<AppHeader tenantName="Nordlys A/S" displayName="Mikkel Holm" roles={["Employee", "Manager"]} />);
    expect(screen.getByRole("link", { name: "Approvals" })).toBeInTheDocument();
  });

  it("shows console link for security staff and tenant admin", () => {
    const { unmount } = render(<AppHeader tenantName="Nordlys A/S" displayName="Jonas" roles={["Employee", "SecurityStaff"]} />);
    expect(screen.getByRole("link", { name: "Console" })).toBeInTheDocument();
    unmount();
    render(<AppHeader tenantName="Nordlys A/S" displayName="Admin" roles={["Employee", "TenantAdmin"]} />);
    expect(screen.getByRole("link", { name: "Console" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Live" })).toBeInTheDocument();
  });

  it("hides console link for plain employees", () => {
    render(<AppHeader tenantName="Nordlys A/S" displayName="Anna" roles={["Employee"]} />);
    expect(screen.queryByRole("link", { name: "Console" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Live" })).not.toBeInTheDocument();
  });

  it("shows people link for employees and hides it for guests", () => {
    const { unmount } = render(<AppHeader tenantName="Nordlys A/S" displayName="Anna" roles={["Employee"]} />);
    expect(screen.getByRole("link", { name: "People" })).toBeInTheDocument();
    unmount();
    render(<AppHeader tenantName="Nordlys A/S" displayName="Gus" roles={["Guest"]} />);
    expect(screen.queryByRole("link", { name: "People" })).not.toBeInTheDocument();
  });

  it("shows onboarding with remaining steps only while pending", () => {
    const { unmount } = render(
      <AppHeader tenantName="Acme" displayName="Ada" roles={["Employee", "TenantAdmin"]} onboardingRemaining={3} />,
    );
    expect(screen.getByRole("link", { name: "Onboarding" })).toHaveTextContent("Onboarding (3 left)");
    expect(screen.getByRole("link", { name: "Administration" })).toHaveAttribute("href", "/admin/identity");
    unmount();
    render(<AppHeader tenantName="Acme" displayName="Ada" roles={["Employee", "TenantAdmin"]} />);
    expect(screen.queryByRole("link", { name: "Onboarding" })).not.toBeInTheDocument();
  });
});
