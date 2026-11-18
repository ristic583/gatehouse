import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AppTitle } from "./app-title";

describe("AppTitle", () => {
  it("renders the product name", () => {
    render(<AppTitle />);
    expect(screen.getByText("Gatehouse")).toBeInTheDocument();
  });
});
