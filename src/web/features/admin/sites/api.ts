import { apiClient } from "@/lib/api/client";
import type {
  AdminFloorDto,
  FloorPlanDto,
  PersonSummaryDto,
  PlanAreaDto,
  PlanDoorDto,
  PlanReaderDto,
  SaveAreaRequest,
  SaveDoorRequest,
  SaveFloorRequest,
  SaveReaderRequest,
  SaveSiteRequest,
  SecurityLevelAdminDto,
  SiteDto,
} from "@/lib/api/types";

const A = "/v1/admin";

export const sitesAdminKeys = {
  sites: ["admin", "sites"] as const,
  plan: (floorId: string) => ["admin", "floor-plan", floorId] as const,
  securityLevels: ["admin", "security-levels"] as const,
};

export const sitesAdminApi = {
  sites: () => apiClient.get<SiteDto[]>("/v1/sites"),
  createSite: (body: SaveSiteRequest) => apiClient.post<SiteDto>(`${A}/sites`, body),
  updateSite: (siteId: string, body: SaveSiteRequest) => apiClient.put<SiteDto>(`${A}/sites/${siteId}`, body),
  deleteSite: (siteId: string) => apiClient.delete<void>(`${A}/sites/${siteId}`),
  createFloor: (siteId: string, body: SaveFloorRequest) => apiClient.post<AdminFloorDto>(`${A}/sites/${siteId}/floors`, body),
  updateFloor: (floorId: string, body: SaveFloorRequest) => apiClient.put<AdminFloorDto>(`${A}/floors/${floorId}`, body),
  deleteFloor: (floorId: string) => apiClient.delete<void>(`${A}/floors/${floorId}`),
  plan: (floorId: string) => apiClient.get<FloorPlanDto>(`${A}/floors/${floorId}/plan`),
  securityLevels: () => apiClient.get<SecurityLevelAdminDto[]>(`${A}/security-levels`),
  createArea: (floorId: string, body: SaveAreaRequest) => apiClient.post<PlanAreaDto>(`${A}/floors/${floorId}/areas`, body),
  updateArea: (areaId: string, body: SaveAreaRequest) => apiClient.put<PlanAreaDto>(`${A}/areas/${areaId}`, body),
  deleteArea: (areaId: string) => apiClient.delete<void>(`${A}/areas/${areaId}`),
  setOwners: (areaId: string, personIds: string[]) => apiClient.put<PlanAreaDto>(`${A}/areas/${areaId}/owners`, { personIds }),
  createDoor: (areaId: string, body: SaveDoorRequest) => apiClient.post<PlanDoorDto>(`${A}/areas/${areaId}/doors`, body),
  updateDoor: (doorId: string, body: SaveDoorRequest) => apiClient.put<PlanDoorDto>(`${A}/doors/${doorId}`, body),
  deleteDoor: (doorId: string) => apiClient.delete<void>(`${A}/doors/${doorId}`),
  createReader: (doorId: string, body: SaveReaderRequest) => apiClient.post<PlanReaderDto>(`${A}/doors/${doorId}/readers`, body),
  updateReader: (readerId: string, body: SaveReaderRequest) => apiClient.put<PlanReaderDto>(`${A}/readers/${readerId}`, body),
  deleteReader: (readerId: string) => apiClient.delete<void>(`${A}/readers/${readerId}`),
  uploadBackground: (floorId: string, file: File) => {
    const form = new FormData();
    form.append("file", file);
    return apiClient.upload<void>("PUT", `${A}/floors/${floorId}/background`, form);
  },
  removeBackground: (floorId: string) => apiClient.delete<void>(`${A}/floors/${floorId}/background`),
  searchEmployees: (query: string) =>
    apiClient.get<PersonSummaryDto[]>(`/v1/people?type=Employee${query.trim() === "" ? "" : `&query=${encodeURIComponent(query.trim())}`}`),
};

const ERROR_TEXT: Record<string, string> = {
  "site.code_taken": "Another site uses this code.",
  "site.name_taken": "Another site uses this name.",
  "site.invalid_code": "Site code must be 2-30 uppercase letters, digits or dashes.",
  "site.invalid_time_zone": "Unknown time zone.",
  "site.not_empty": "Remove the site's floors first.",
  "floor.level_taken": "Another floor of this site has this level.",
  "floor.invalid_plan_size": "The plan size is invalid or smaller than existing areas.",
  "floor.not_empty": "Remove the floor's areas first.",
  "area.invalid_polygon": "The polygon needs at least 3 points inside the plan and must not cross itself.",
  "area.overlaps": "The area overlaps another area on this floor.",
  "area.code_taken": "Another area uses this code.",
  "area.name_taken": "Another area on this floor uses this name.",
  "area.invalid_code": "Area code must be 2-30 uppercase letters, digits or dashes.",
  "area.last_owner": "An area needs at least one owner.",
  "area.owner_not_employee": "Owners must be employees.",
  "area.in_use": "The area still has access records and cannot be deleted.",
  "door.invalid_position": "The door must lie inside the floor plan.",
  "door.ref_taken": "Another door of the same PACS instance uses this reference.",
  "reader.invalid_position": "The reader must lie inside the floor plan.",
  "floor.background_too_large": "The drawing is larger than 10 MiB.",
  "floor.background_unsupported_type": "Only JPEG and PNG drawings are accepted.",
};

export function sitesAdminError(code: string | undefined, fallback: string): string {
  return (code !== undefined ? ERROR_TEXT[code] : undefined) ?? fallback;
}
