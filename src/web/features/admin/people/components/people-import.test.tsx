import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { PeopleImportResultDto } from "@/lib/api/types";
import { PeopleImport } from "./people-import";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

describe("PeopleImport", () => {
  it("dry run shows line errors before import", async () => {
    const withErrors: PeopleImportResultDto = {
      dryRun: true,
      created: 2,
      updated: 0,
      unchanged: 0,
      errors: [{ line: 3, code: "person.invalid_email", field: "email" }],
    };
    const clean: PeopleImportResultDto = { dryRun: true, created: 2, updated: 0, unchanged: 0, errors: [] };
    const done: PeopleImportResultDto = { ...clean, dryRun: false };
    const importCsv = vi
      .fn<(file: File, dryRun: boolean) => Promise<PeopleImportResultDto>>()
      .mockResolvedValueOnce(withErrors)
      .mockResolvedValueOnce(clean)
      .mockResolvedValueOnce(done);
    render(
      <QueryClientProvider client={new QueryClient()}>
        <PeopleImport importCsv={importCsv} />
      </QueryClientProvider>,
    );

    const file = new File(["email,given_name,family_name\n"], "people.csv", { type: "text/csv" });
    fireEvent.change(screen.getByLabelText("CSV file"), { target: { files: [file] } });
    const importButton = screen.getByRole("button", { name: "Import" });
    expect(importButton).toBeDisabled();

    fireEvent.click(screen.getByRole("button", { name: "Preview" }));
    expect(await screen.findByTestId("import-preview")).toHaveTextContent("1 errors");
    expect(screen.getByTestId("import-error")).toHaveTextContent("Line 3 (email)");
    expect(importButton).toBeDisabled();

    fireEvent.click(screen.getByRole("button", { name: "Preview" }));
    await waitFor(() => expect(screen.queryByTestId("import-error")).not.toBeInTheDocument());
    expect(importButton).toBeEnabled();
    fireEvent.click(importButton);
    expect(await screen.findByTestId("import-result")).toHaveTextContent("2 new");
    expect(importCsv.mock.calls.map((c) => c[1])).toEqual([true, true, false]);
  });
});
