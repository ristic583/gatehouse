import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { CAPABILITY_GAP_LABELS, capabilityGapLabel } from "./capability-gaps";
import { CapabilityGapList } from "./components/capability-gap-list";

describe("capability gaps", () => {
  it("labels every gap", () => {
    expect(Object.keys(CAPABILITY_GAP_LABELS)).toHaveLength(6);
    expect(capabilityGapLabel("Lockdown")).toBe("No lockdown");
  });

  it("renders a chip per gap", () => {
    render(<CapabilityGapList gaps={["NativeValidity", "Lockdown"]} />);
    const chips = screen.getAllByTestId("capability-gap");
    expect(chips).toHaveLength(2);
    expect(chips[0]).toHaveAttribute("data-gap", "NativeValidity");
    expect(chips[0]).toHaveTextContent("No native end dates (agent removes access)");
    expect(chips[1]).toHaveTextContent("No lockdown");
  });

  it("renders nothing when gaps unknown", () => {
    const { container } = render(<CapabilityGapList gaps={null} />);
    expect(container).toBeEmptyDOMElement();
    render(<CapabilityGapList gaps={[]} />);
    expect(screen.queryByTestId("capability-gap")).not.toBeInTheDocument();
  });
});
