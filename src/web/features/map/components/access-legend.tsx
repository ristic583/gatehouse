import { ACCESS_STATE_META, ACCESS_STATES } from "../access-state";

export function AccessLegend() {
  return (
    <ul aria-label="Legend" className="flex flex-wrap gap-4 text-sm">
      {ACCESS_STATES.map((state) => {
        const meta = ACCESS_STATE_META[state];
        const Icon = meta.icon;
        return (
          <li key={state} className="flex items-center gap-1.5">
            <Icon className="size-4" style={{ color: `var(${meta.colorVar})` }} aria-hidden="true" />
            <span>{meta.label}</span>
          </li>
        );
      })}
    </ul>
  );
}
