import type { CreateTenantRequest } from "@/lib/api/types";

/** Mirrors `CreateTenantValidator` and `TenantId`/`Tenant` rules on the server; the server stays authoritative. */
export const TENANT_ID_PATTERN = /^[a-z][a-z0-9-]{2,31}$/;
export const EMAIL_DOMAIN_PATTERN = /^(?=.{4,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{0,62}$/;
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+$/;

export type CreateTenantErrors = Partial<Record<keyof CreateTenantRequest, string>>;

export function validateOrganization(form: CreateTenantRequest): CreateTenantErrors {
  const errors: CreateTenantErrors = {};
  if (!TENANT_ID_PATTERN.test(form.tenantId)) {
    errors.tenantId = "3-32 characters: a lowercase letter followed by lowercase letters, digits or dashes.";
  }
  if (form.displayName.trim() === "" || form.displayName.length > 100) {
    errors.displayName = "Enter a display name (at most 100 characters).";
  }
  if (!EMAIL_DOMAIN_PATTERN.test(form.emailDomain)) {
    errors.emailDomain = "A lowercase DNS name with a dot, such as example.com.";
  }
  if (form.timeZoneId.trim() === "") {
    errors.timeZoneId = "Choose a time zone.";
  }
  return errors;
}

export function validateFirstAdmin(form: CreateTenantRequest): CreateTenantErrors {
  const errors: CreateTenantErrors = {};
  const email = form.adminEmail.trim();
  if (email.length === 0 || email.length > 254 || !EMAIL_PATTERN.test(email)) {
    errors.adminEmail = "Enter a valid email address.";
  } else if (!email.toLowerCase().endsWith(`@${form.emailDomain.toLowerCase()}`)) {
    errors.adminEmail = `The admin email must be in the tenant's email domain (@${form.emailDomain}).`;
  }
  if (form.adminGivenName.trim() === "" || form.adminGivenName.length > 100) {
    errors.adminGivenName = "Enter a given name.";
  }
  if (form.adminFamilyName.trim() === "" || form.adminFamilyName.length > 100) {
    errors.adminFamilyName = "Enter a family name.";
  }
  return errors;
}
