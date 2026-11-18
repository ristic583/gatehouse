import { Check, Circle, Clock3, Radio, ShieldCheck, UserRound } from "lucide-react";

export function FlowSteps({ chain = "Both", active = 0 }: { chain?: "Manager" | "AreaOwner" | "Both"; active?: number }) {
  const steps = [
    { label: "Request", icon: UserRound },
    ...(chain !== "AreaOwner" ? [{ label: "Manager", icon: ShieldCheck }] : []),
    ...(chain !== "Manager" ? [{ label: "Area owner", icon: ShieldCheck }] : []),
    { label: "Applied", icon: Radio },
  ];
  return (
    <ol className="gh-flow" aria-label="Access approval path">
      {steps.map(({ label, icon: Icon }, i) => (
        <li key={label} className={i < active ? "is-done" : i === active ? "is-current" : ""}>
          <span>{i < active ? <Check size={16} /> : i === active ? <Clock3 size={16} /> : <Icon size={16} />}</span>
          <strong>{label}</strong>
          <small>{i < active ? "Complete" : i === active ? "In progress" : "Next step"}</small>
        </li>
      ))}
    </ol>
  );
}
export function EmptyState({ title, description }: { title: string; description: string }) {
  return (
    <div className="gh-empty">
      <span>
        <Circle size={28} />
      </span>
      <h2>{title}</h2>
      <p>{description}</p>
    </div>
  );
}
