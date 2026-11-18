import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AppHeader } from "./app-header";
import { shellForPath, TenantShellSlot } from "./app-shell";

const navigation = vi.hoisted(() => ({ pathname: "/map" }));
vi.mock("next/navigation", () => ({
  usePathname: () => navigation.pathname,
  useSearchParams: () => new URLSearchParams(),
  useRouter: () => ({ push: vi.fn(), refresh: vi.fn() }),
}));

function renderShell() {
  return render(
    <TenantShellSlot>
      <AppHeader tenantName="Nordlys A/S" displayName="Admin" roles={["Employee", "TenantAdmin"]} />
    </TenantShellSlot>,
  );
}

describe("app shell", () => {
  beforeEach(() => {
    navigation.pathname = "/map";
  });

  it("platform routes never render the tenant sidebar", () => {
    for (const path of ["/platform", "/platform/tenants/acme", "/platform/tenants/new"]) {
      navigation.pathname = path;
      const { container, unmount } = renderShell();
      expect(container.querySelector(".gh-sidebar")).toBeNull();
      expect(screen.queryByTestId("current-tenant")).not.toBeInTheDocument();
      unmount();
    }
  });

  it("tenant routes keep the tenant sidebar", () => {
    const { container } = renderShell();
    expect(container.querySelector(".gh-sidebar")).not.toBeNull();
    expect(screen.getByTestId("current-tenant")).toHaveTextContent("Nordlys A/S");
  });

  it("chooses the shell by path prefix only", () => {
    expect(shellForPath("/platform")).toBe("platform");
    expect(shellForPath("/platform/tenants")).toBe("platform");
    expect(shellForPath("/platforms")).toBe("tenant");
    expect(shellForPath("/admin/platform")).toBe("tenant");
    expect(shellForPath(null)).toBe("tenant");
  });
});
