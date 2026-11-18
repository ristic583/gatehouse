import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CreateTenantWizard } from "./create-tenant-wizard";
import { platformTenant } from "./test-data";

const fetchMock = vi.fn<typeof fetch>();
const json = (body: unknown, status = 200) =>
  Promise.resolve(new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));

function renderWizard() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <CreateTenantWizard pollMs={10} />
    </QueryClientProvider>,
  );
}

function fill(label: string, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

function fillOrganization() {
  fill("Tenant id", "acme");
  fill("Display name", "Acme Corp");
  fill("Email domain", "acme.test");
  fill("Time zone (IANA)", "Europe/Copenhagen");
  fireEvent.click(screen.getByRole("button", { name: "Next" }));
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("CreateTenantWizard", () => {
  it("validates the admin email domain", () => {
    renderWizard();
    fillOrganization();

    fill("Admin email", "admin@other.test");
    fill("Given name", "Ada");
    fill("Family name", "Admin");
    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(screen.getByText(/must be in the tenant's email domain/)).toBeInTheDocument();
    expect(screen.queryByTestId("create-tenant-review")).not.toBeInTheDocument();

    fill("Admin email", "admin@acme.test");
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(screen.getByTestId("create-tenant-review")).toHaveTextContent("admin@acme.test");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("shows provisioning progress until active", async () => {
    const running = (step: "Database" | "Baseline") =>
      platformTenant({
        status: "Provisioning",
        provisioningStep: step,
        provisioning: {
          step,
          state: "Running",
          attempts: 1,
          lastError: null,
          startedAt: "2026-11-14T10:00:00Z",
          completedAt: null,
          adminInvitedAt: null,
          adminActivatedAt: null,
        },
      });
    const responses = [running("Baseline"), platformTenant()];
    fetchMock.mockImplementation((input, init) => {
      if (init?.method === "POST") return json(running("Database"), 202);
      return json(responses.length > 1 ? responses.shift() : responses[0]);
    });

    renderWizard();
    fillOrganization();
    fill("Admin email", "admin@acme.test");
    fill("Given name", "Ada");
    fill("Family name", "Admin");
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    fireEvent.click(screen.getByRole("button", { name: "Create tenant" }));

    expect(await screen.findByTestId("provisioning-progress")).toBeInTheDocument();
    const body = JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body)) as Record<string, string>;
    expect(body).toMatchObject({ tenantId: "acme", emailDomain: "acme.test", adminEmail: "admin@acme.test" });
    await waitFor(() => expect(screen.getByTestId("provisioning-step-Baseline")).toHaveAttribute("data-state", "current"));
    expect(screen.getByTestId("provisioning-step-Database")).toHaveAttribute("data-state", "done");

    expect(await screen.findByTestId("provisioning-done")).toBeInTheDocument();
    expect(screen.getByTestId("provisioning-step-AdminInvite")).toHaveAttribute("data-state", "done");
    const polls = fetchMock.mock.calls.length;
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(fetchMock.mock.calls.length).toBe(polls);
  });
});
