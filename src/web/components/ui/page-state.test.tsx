import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api/client";
import { FORBIDDEN_TEXT, PageState, QueryErrorState } from "./page-state";

describe("PageState", () => {
  it("renders each state", () => {
    const retry = vi.fn();
    const { rerender } = render(<PageState kind="loading" message="Loading sites…" />);
    expect(screen.getByRole("status")).toHaveTextContent("Loading sites…");

    rerender(<PageState kind="empty" message="No sites yet." hint="Add a site with the form above." />);
    expect(screen.getByTestId("page-state")).toHaveAttribute("data-kind", "empty");
    expect(screen.getByText("Add a site with the form above.")).toBeInTheDocument();

    rerender(<PageState kind="error" message="Sites could not be loaded." onRetry={retry} />);
    expect(screen.getByRole("alert")).toHaveTextContent("Sites could not be loaded.");
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(retry).toHaveBeenCalledTimes(1);

    rerender(<PageState kind="forbidden" />);
    expect(screen.getByRole("alert")).toHaveTextContent(FORBIDDEN_TEXT);
  });

  it("forbidden has no retry", () => {
    const retry = vi.fn();
    render(<QueryErrorState error={new ApiError(403, "auth.forbidden", "Forbidden")} message="Could not load." onRetry={retry} />);
    expect(screen.getByRole("alert")).toHaveTextContent(FORBIDDEN_TEXT);
    expect(screen.queryByRole("button", { name: "Retry" })).not.toBeInTheDocument();
  });
});
