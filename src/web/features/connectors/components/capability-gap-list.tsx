import { CircleSlash } from "lucide-react";
import type { CapabilityGap } from "@/lib/api/types";
import { capabilityGapLabel } from "../capability-gaps";

export interface CapabilityGapListProps {
  gaps: readonly CapabilityGap[] | null | undefined;
}

/** One chip per capability gap (icon + text); renders nothing when gaps are unknown or empty. */
export function CapabilityGapList({ gaps }: CapabilityGapListProps) {
  if (!gaps || gaps.length === 0) {
    return null;
  }
  return (
    <ul aria-label="Capability gaps" className="flex flex-wrap gap-1">
      {gaps.map((gap) => (
        <li
          key={gap}
          data-testid="capability-gap"
          data-gap={gap}
          className="flex items-center gap-1 rounded border border-amber-300 bg-amber-50 px-1.5 text-xs text-amber-900"
        >
          <CircleSlash aria-hidden="true" className="size-3" />
          {capabilityGapLabel(gap)}
        </li>
      ))}
    </ul>
  );
}
