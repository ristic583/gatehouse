import { apiClient } from "@/lib/api/client";
import type { RetentionRunResult, RetentionSettingsDto } from "@/lib/api/types";

export const retentionKeys = { settings: ["retention", "settings"] as const };

export const retentionApi = {
  settings: () => apiClient.get<RetentionSettingsDto>("/v1/admin/retention/settings"),
  set: (key: string, days: number) => apiClient.put<RetentionSettingsDto>("/v1/admin/retention/settings", { key, days }),
  run: () => apiClient.post<RetentionRunResult>("/v1/admin/retention/run"),
};

export const RETENTION_SETTINGS: ReadonlyArray<{
  field: keyof RetentionSettingsDto;
  key: string;
  label: string;
  min: number;
  max: number;
}> = [
  {
    field: "photoDaysAfterTermination",
    key: "retention.photo_days_after_termination",
    label: "Delete photos after termination (days)",
    min: 0,
    max: 3650,
  },
  {
    field: "personalDataDaysAfterTermination",
    key: "retention.personal_data_days_after_termination",
    label: "Anonymize people after termination (days)",
    min: 30,
    max: 3650,
  },
  { field: "doorEventDays", key: "retention.door_event_days", label: "Keep door events (days)", min: 7, max: 365 },
];
