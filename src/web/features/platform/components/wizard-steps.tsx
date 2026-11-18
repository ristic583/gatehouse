import { Check, Circle, Clock3 } from "lucide-react";

/** Generic step indicator on the design system's `gh-flow` styles (the design `FlowSteps` is fixed to approval chains). */
export function WizardSteps({ steps, active, label }: { steps: readonly string[]; active: number; label: string }) {
  return (
    <ol className="gh-flow" aria-label={label}>
      {steps.map((step, i) => (
        <li
          key={step}
          className={i < active ? "is-done" : i === active ? "is-current" : ""}
          aria-current={i === active ? "step" : undefined}
        >
          <span>{i < active ? <Check size={16} /> : i === active ? <Clock3 size={16} /> : <Circle size={16} />}</span>
          <strong>{step}</strong>
          <small>{i < active ? "Complete" : i === active ? "Current" : "Next"}</small>
        </li>
      ))}
    </ol>
  );
}
