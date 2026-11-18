import { ApiError } from "@/lib/api/client";

const ERROR_LABELS: Record<string, string> = {
  "person.sponsorship_too_long": "Longer than the tenant allows for this type",
  "person.email_taken": "A person with this email already exists",
  "person.invalid_email": "The email address is not valid",
  "person.invalid_name": "Names must be 1-100 characters",
  "person.terminated": "This person is terminated",
  "person.not_local": "Only locally managed people can be changed here",
  "card.invalid_transition": "The card cannot change to that state now",
  "card.reason_required": "A reason is required",
  "magic_link.guest_only": "Magic links are only for active guests",
  "auth.forbidden": "You are not allowed to do this",
};

/** A readable message for an API error code, falling back to the server's detail. */
export function errorLabel(code: string | undefined, detail: string): string {
  return (code !== undefined ? ERROR_LABELS[code] : undefined) ?? detail;
}

export const PERSON_TYPES = ["Employee", "External", "Visitor", "Guest"] as const;
export const ONBOARD_TYPES = ["External", "Visitor", "Guest"] as const;

export function formatDate(iso: string | null): string {
  return iso === null ? "open-ended" : new Date(iso).toLocaleDateString("en-GB", { year: "numeric", month: "short", day: "numeric" });
}

export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString("en-GB", { year: "numeric", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });
}

/** User-facing message of a failed people request. */
export const messageOf = (error: Error | null) => (error instanceof ApiError ? errorLabel(error.code, error.detail) : error?.message);
