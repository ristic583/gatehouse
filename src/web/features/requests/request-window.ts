import { toDateInput, toRequestWindow } from "./date-window";
export function addCalendarDays(value: string, days: number): string {
  const [y, m, d] = value.split("-").map(Number);
  return toDateInput(new Date(y!, m! - 1, d! + days));
}
function validDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const [y, m, d] = value.split("-").map(Number);
  return toDateInput(new Date(y!, m! - 1, d!)) === value;
}
export function validateRequestWindow(start: string, end: string, justification: string, maxDays: number, now: Date): string | null {
  if (!validDate(start) || !validDate(end)) return "Choose a valid start and end date.";
  if (start < toDateInput(now)) return "The start date cannot be in the past.";
  if (end < start) return "The end date must be on or after the start date.";
  const window = toRequestWindow(start, end, now);
  if (Date.parse(window.requestedEnd) - Date.parse(window.requestedStart) > maxDays * 86400000)
    return `This space allows a maximum of ${maxDays} days. Shorten the access window.`;
  if (justification.trim().length < 10) return "Explain why access is needed in at least 10 characters.";
  if (justification.trim().length > 500) return "Keep your explanation within 500 characters.";
  return null;
}
/** Display an exact instant with the viewer's UTC offset; the API retains UTC timestamps. */
export function formatAccessTime(iso: string): string {
  return new Intl.DateTimeFormat(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    timeZoneName: "shortOffset",
  }).format(new Date(iso));
}
