/** Parses `yyyy-mm-dd` as a local calendar date at midnight. */
function localMidnight(date: string): Date {
  const [y, m, d] = date.split("-").map(Number);
  return new Date(y ?? 1970, (m ?? 1) - 1, d ?? 1);
}

function sameLocalDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

/**
 * Converts the form's calendar dates to an API window: start is `now` when the start date is today, otherwise local
 * midnight of the start date; end is local midnight of the day after the end date (exclusive).
 */
export function toRequestWindow(startDate: string, endDate: string, now: Date): { requestedStart: string; requestedEnd: string } {
  const startDay = localMidnight(startDate);
  const start = sameLocalDay(startDay, now) ? now : startDay;
  const endDay = localMidnight(endDate);
  const end = new Date(endDay.getFullYear(), endDay.getMonth(), endDay.getDate() + 1);
  return { requestedStart: start.toISOString(), requestedEnd: end.toISOString() };
}

export function toDateInput(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}
