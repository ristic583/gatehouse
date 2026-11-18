import { apiClient } from "@/lib/api/client";
import type { HrSyncResult, LifecycleRunResult, LifecycleSettingsDto } from "@/lib/api/types";

export const lifecycleKeys = { settings: ["lifecycle", "settings"] as const };

export const lifecycleApi = {
  settings: () => apiClient.get<LifecycleSettingsDto>("/v1/admin/lifecycle/settings"),
  setSetting: (key: string, days: number) => apiClient.put<LifecycleSettingsDto>("/v1/admin/lifecycle/settings", { key, days }),
  run: () => apiClient.post<LifecycleRunResult>("/v1/admin/lifecycle/run"),
  hrSync: () => apiClient.post<HrSyncResult>("/v1/admin/hr-sync"),
};

export const SETTINGS: ReadonlyArray<{ field: keyof LifecycleSettingsDto; key: string; label: string }> = [
  { field: "maxSponsorshipDays", key: "lifecycle.max_sponsorship_days", label: "Max sponsorship (days)" },
  { field: "maxVisitorDays", key: "lifecycle.max_visitor_days", label: "Max visitor stay (days)" },
  { field: "renewalEmployeeDays", key: "lifecycle.renewal_employee_days", label: "Employee renewal interval (days)" },
  { field: "renewalExternalDays", key: "lifecycle.renewal_external_days", label: "External renewal interval (days)" },
  { field: "renewalGuestDays", key: "lifecycle.renewal_guest_days", label: "Guest renewal interval (days)" },
  { field: "renewalLeadDays", key: "lifecycle.renewal_lead_days", label: "Renewal lead time (days)" },
];
