import { apiClient } from "@/lib/api/client";
import type {
  AssignRoleRequest,
  CreateEmployeeRequest,
  PeopleImportResultDto,
  PersonDetailDto,
  PersonSummaryDto,
  RoleAssignmentDto,
  SiteDto,
} from "@/lib/api/types";

export const peopleAdminKeys = {
  roles: ["admin", "role-assignments"] as const,
  employees: (query: string) => ["admin", "employees", query] as const,
  sites: ["admin", "sites-for-roles"] as const,
};

export const peopleAdminApi = {
  createEmployee: (body: CreateEmployeeRequest) => apiClient.post<PersonDetailDto>("/v1/admin/people/employees", body),
  importCsv: (file: File, dryRun: boolean) => {
    const form = new FormData();
    form.append("file", file);
    return apiClient.upload<PeopleImportResultDto>("POST", `/v1/admin/people/import?dryRun=${dryRun}`, form);
  },
  roles: () => apiClient.get<RoleAssignmentDto[]>("/v1/admin/role-assignments"),
  assignRole: (body: AssignRoleRequest) => apiClient.post<RoleAssignmentDto>("/v1/admin/role-assignments", body),
  removeRole: (id: string) => apiClient.delete<void>(`/v1/admin/role-assignments/${id}`),
  searchEmployees: (query: string) =>
    apiClient.get<PersonSummaryDto[]>(`/v1/people?type=Employee${query.trim() === "" ? "" : `&query=${encodeURIComponent(query.trim())}`}`),
  sites: () => apiClient.get<SiteDto[]>("/v1/sites"),
};

/** Human text for M10 people/role error codes (`domain-model.md` §4). */
export function peopleAdminError(code: string | undefined, fallback: string): string {
  switch (code) {
    case "person.email_taken":
      return "A person with this email already exists.";
    case "person.invalid_email":
      return "The email address is not valid.";
    case "person.not_found":
      return "The person (or manager) was not found.";
    case "person.invalid_name":
      return "Given and family name are required.";
    case "role.last_tenant_admin":
      return "The last tenant admin cannot be removed. Assign another admin first.";
    case "role.assignment_exists":
      return "The person already has this role.";
    case "role.person_not_employee":
      return "Roles can only be assigned to active employees.";
    case "people_import.too_large":
      return "The file is too large.";
    default:
      return fallback;
  }
}
