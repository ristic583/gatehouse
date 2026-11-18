import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { SpatialBuildingProps } from "./spatial-building";
import { createSampleMaps, createSampleRequests, sampleDetails, sampleMe, sampleSites } from "@/lib/fixtures/sample-workspace";
import { MapView } from "./map-view";
const route = vi.hoisted(() => ({ url: "/map", listeners: new Set<() => void>() }));
vi.mock("next/navigation", async () => {
  const { useSyncExternalStore } = await import("react");
  const subscribe = (cb: () => void) => {
    route.listeners.add(cb);
    return () => {
      route.listeners.delete(cb);
    };
  };
  return {
    usePathname: () => "/map",
    useSearchParams: () =>
      new URLSearchParams(
        useSyncExternalStore(
          subscribe,
          () => route.url,
          () => route.url,
        ).split("?")[1],
      ),
    useRouter: () => ({
      replace: (url: string) => {
        route.url = url;
        route.listeners.forEach((cb) => cb());
      },
    }),
  };
});
vi.mock("./spatial-building", () => ({
  SpatialBuilding: (props: SpatialBuildingProps) => (
    <div data-testid="scene" data-floor={props.activeFloorId} data-view={props.view} data-camera={props.cameraPreset} />
  ),
}));
function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}
const maps = createSampleMaps();
function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MapView />
    </QueryClientProvider>,
  );
}
beforeEach(() => {
  route.url = "/map";
  localStorage.clear();
  vi.stubGlobal(
    "matchMedia",
    vi.fn(() => ({ matches: false, addEventListener() {}, removeEventListener() {} })),
  );
  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL) => {
      const p = String(input);
      return Promise.resolve(
        json(
          p.endsWith("/me")
            ? { ...sampleMe, roles: ["Employee"] }
            : p.endsWith("/sites")
              ? sampleSites
              : p.endsWith("/access-requests")
                ? createSampleRequests()
                : p.includes("/access-map")
                  ? maps.find((m) => p.includes(`/floors/${m.floorId}/`))
                  : p.includes("/areas/")
                    ? sampleDetails(maps, p.split("/").at(-1)!)
                    : { terms: {} },
        ),
      );
    }),
  );
});
afterEach(() => {
  vi.unstubAllGlobals();
  route.listeners.clear();
});
describe("task-first map", () => {
  it("starts with one floor and restores the signed-in user's last floor", async () => {
    const first = mount();
    expect(await screen.findByTestId("scene")).toHaveAttribute("data-view", "isolated");
    fireEvent.change(screen.getByLabelText("Floor"), { target: { value: "first" } });
    await waitFor(() => expect(screen.getByTestId("scene")).toHaveAttribute("data-floor", "first"));
    first.unmount();
    route.url = "/map";
    mount();
    await waitFor(() => expect(screen.getByTestId("scene")).toHaveAttribute("data-floor", "first"));
  });
  it("finds a room on another floor, focuses it and reveals the policy", async () => {
    mount();
    await screen.findByTestId("scene");
    fireEvent.change(screen.getByLabelText("Search this building"), { target: { value: "Research lab" } });
    const result = await screen.findByRole("button", { name: /Research lab.*First floor.*View room/ });
    fireEvent.keyDown(screen.getByLabelText("Search this building"), { key: "ArrowDown" });
    expect(result).toHaveFocus();
    fireEvent.click(result);
    await waitFor(() => expect(screen.getByTestId("scene")).toHaveAttribute("data-floor", "first"));
    expect(screen.getByTestId("scene")).toHaveAttribute("data-camera", "room");
    const inspector = await screen.findByRole("region", { name: "Research lab access details" });
    expect(inspector).toHaveTextContent("Approval policy");
    expect(within(inspector).getByRole("link", { name: "Track request" })).toHaveAttribute("href", "/requests?area=first-2");
  });
  it("uses a room list on mobile and lets the user explicitly opt into 3D", async () => {
    vi.stubGlobal(
      "matchMedia",
      vi.fn(() => ({ matches: true, addEventListener() {}, removeEventListener() {} })),
    );
    mount();
    expect(await screen.findByRole("button", { name: "Room list" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.queryByTestId("scene")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "3D model" }));
    expect(await screen.findByTestId("scene")).toHaveAttribute("data-view", "isolated");
    expect(screen.queryByRole("link", { name: /Review approvals/ })).not.toBeInTheDocument();
  });
  it("selects rooms in the floor plan without unexpectedly opening a request", async () => {
    route.url = "/map?mode=plan";
    mount();
    await screen.findByTestId("floor-map");
    fireEvent.click(screen.getByTestId("area-GROUND-2"));
    const inspector = await screen.findByRole("region", { name: "Meeting suite access details" });
    expect(within(inspector).getByRole("button", { name: "Request access" })).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
  it("clears a persisted invalid floor and respects explicit URL selection", async () => {
    localStorage.setItem(
      `gh:map:${encodeURIComponent(sampleMe.tenantId)}:${encodeURIComponent(sampleMe.subject)}`,
      JSON.stringify({ siteId: "missing", floorId: "missing" }),
    );
    route.url = "/map?site=copenhagen&floor=second";
    mount();
    await waitFor(() => expect(screen.getByTestId("scene")).toHaveAttribute("data-floor", "second"));
    await act(async () => {
      route.url = "/map";
      route.listeners.forEach((cb) => cb());
    });
    await waitFor(() => expect(screen.getByTestId("scene")).toHaveAttribute("data-floor", "ground"));
  });
});
