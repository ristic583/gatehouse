import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ConsoleView } from "./console-view";

let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: vi.fn() }),
  usePathname: () => "/console",
  useSearchParams: () => new URLSearchParams(search),
}));

const fetchMock = vi.fn<typeof fetch>();

function respond(roles: string[]) {
  fetchMock.mockImplementation((input) => {
    const url = String(input);
    const body = url.includes("/v1/me") ? { roles } : [];
    return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } }));
  });
}

function renderConsole() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <ConsoleView />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  search = "";
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("ConsoleView", () => {
  it("tenant admin does not see the fulfillment tab", async () => {
    respond(["Employee", "TenantAdmin"]);
    renderConsole();
    expect(await screen.findByRole("tab", { name: "Tasks" })).toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "Fulfillment" })).not.toBeInTheDocument();
  });

  it("tab is read from the url", async () => {
    search = "tab=audit";
    respond(["Employee", "SecurityStaff"]);
    renderConsole();
    const audit = await screen.findByRole("tab", { name: "Audit" });
    expect(audit).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Fulfillment" })).toHaveAttribute("aria-selected", "false");
  });

  it("security staff and tenant admin see the reconciliation tab", async () => {
    respond(["Employee", "TenantAdmin"]);
    renderConsole();
    expect(await screen.findByRole("tab", { name: "Reconciliation" })).toBeInTheDocument();
  });

  it("employees without staff or admin role see a not-permitted message", async () => {
    respond(["Employee"]);
    renderConsole();
    expect(await screen.findByRole("alert")).toHaveTextContent("not permitted");
    expect(screen.queryByRole("tab", { name: "Tasks" })).not.toBeInTheDocument();
  });
});
