/** Presentation defaults only; backend policies remain authoritative. */
export const APPROVER_ROLES = ["Manager", "AreaOwner", "Sponsor"];
export const OPERATIONS_ROLES = ["SecurityStaff", "TenantAdmin"];
export function homeForRoles(roles: readonly string[]): string {
  if (roles.some((r) => OPERATIONS_ROLES.includes(r))) return "/dashboard";
  if (roles.some((r) => APPROVER_ROLES.includes(r))) return "/approvals";
  return "/map";
}
/** Tenant admin area (M10); `/admin/*` pages highlight this nav item. */
export const ADMIN_HOME = "/admin/identity";
export const ONBOARDING_HOME = "/onboarding";

/** `/` entry: a tenant admin with pending onboarding goes to the guided flow; deep links never pass through here. */
export function entryFor(me: { roles: readonly string[]; onboardingPending: boolean }): string {
  if (me.onboardingPending && me.roles.includes("TenantAdmin")) return ONBOARDING_HOME;
  return homeForRoles(me.roles);
}
