import { DoorOpen } from "lucide-react";

export function AppTitle() {
  return (
    <span className="gh-brand">
      <span className="gh-brand-mark">
        <DoorOpen size={21} strokeWidth={1.7} />
      </span>
      <span>Gatehouse</span>
      <span className="gh-brand-dot">.</span>
    </span>
  );
}
