import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PersonSummaryDto } from "@/lib/api/types";
import { PeopleList } from "./people-list";

const fetchMock = vi.fn<typeof fetch>();

const anna: PersonSummaryDto = {
  personId: "p-1",
  displayName: "Anna Larsen",
  email: "anna.larsen@nordlys.test",
  type: "External",
  source: "Local",
  status: "Active",
  eligibilityStart: "2026-11-01T00:00:00Z",
  eligibilityEnd: "2026-12-01T00:00:00Z",
  sponsor: { personId: "p-9", displayName: "Mikkel Holm" },
  manager: null,
};

function json(body: unknown, status = 200) {
  return Promise.resolve(new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));
}

function renderList(onSelect = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <PeopleList selectedId={null} onSelect={onSelect} />
    </QueryClientProvider>,
  );
  return onSelect;
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("PeopleList", () => {
  it("lists people and selects a row", async () => {
    fetchMock.mockImplementation(() => json([anna]));
    const onSelect = renderList();

    const rows = await screen.findAllByTestId("person-row");
    fireEvent.click(rows[0]!);

    expect(rows).toHaveLength(1);
    expect(onSelect).toHaveBeenCalledWith("p-1");
  });

  it("sends search and type filter to the API", async () => {
    fetchMock.mockImplementation(() => json([]));
    renderList();
    await screen.findByText("No people to show.");

    fireEvent.change(screen.getByLabelText("Type filter"), { target: { value: "Guest" } });
    fireEvent.change(screen.getByLabelText("Search people"), { target: { value: "anna" } });

    await vi.waitFor(() =>
      expect(fetchMock.mock.calls.some(([url]) => String(url).includes("query=anna") && String(url).includes("type=Guest"))).toBe(true),
    );
  });

  it("shows onboarding errors inline", async () => {
    fetchMock.mockImplementation((_input, init) =>
      init?.method === "POST" ? json({ code: "person.sponsorship_too_long", title: "Too long", status: 409 }, 409) : json([]),
    );
    renderList();
    fireEvent.click(await screen.findByRole("button", { name: "Add person" }));
    const dialog = screen.getByTestId("onboard-dialog");
    fireEvent.change(screen.getByLabelText("Given name"), { target: { value: "Ola" } });
    fireEvent.change(screen.getByLabelText("Family name"), { target: { value: "Nordmann" } });
    fireEvent.change(screen.getByLabelText("Email"), { target: { value: "ola@x.test" } });
    fireEvent.change(screen.getByLabelText("Start"), { target: { value: "2026-11-10" } });
    fireEvent.change(screen.getByLabelText("End"), { target: { value: "2027-11-10" } });
    fireEvent.submit(dialog.querySelector("form")!);

    expect(await screen.findByRole("alert")).toHaveTextContent("Longer than the tenant allows for this type");
  });
});
