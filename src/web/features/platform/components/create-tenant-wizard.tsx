"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api/client";
import type { CreateTenantRequest, PlatformTenantDto } from "@/lib/api/types";
import { platformApi, platformKeys } from "../api";
import { type CreateTenantErrors, validateFirstAdmin, validateOrganization } from "../validation";
import { ProvisioningProgress } from "./provisioning-progress";
import { WizardSteps } from "./wizard-steps";

export { PROVISIONING_STEPS } from "./provisioning-progress";

const STEPS = ["Organization", "First admin", "Review"] as const;

function defaultTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
  } catch {
    return "UTC";
  }
}

const EMPTY: CreateTenantRequest = {
  tenantId: "",
  displayName: "",
  emailDomain: "",
  adminEmail: "",
  adminGivenName: "",
  adminFamilyName: "",
  timeZoneId: "",
};

function Field({
  id,
  label,
  value,
  error,
  onChange,
  type = "text",
  hint,
}: {
  id: keyof CreateTenantRequest;
  label: string;
  value: string;
  error?: string;
  onChange: (value: string) => void;
  type?: string;
  hint?: string;
}) {
  return (
    <div className="flex flex-col gap-1">
      <Label htmlFor={`tenant-${id}`}>{label}</Label>
      <Input
        id={`tenant-${id}`}
        type={type}
        value={value}
        aria-invalid={error !== undefined}
        aria-describedby={error !== undefined ? `tenant-${id}-error` : undefined}
        onChange={(event) => onChange(event.target.value)}
      />
      {hint !== undefined && error === undefined && <small className="text-xs text-muted-foreground">{hint}</small>}
      {error !== undefined && (
        <small id={`tenant-${id}-error`} role="alert" className="text-xs text-destructive">
          {error}
        </small>
      )}
    </div>
  );
}

/** Organization → First admin → Review, then live provisioning progress (polls `GET /tenants/{id}`). */
export function CreateTenantWizard({ pollMs = 2000 }: { pollMs?: number }) {
  const queryClient = useQueryClient();
  const [step, setStep] = useState(0);
  const [form, setForm] = useState<CreateTenantRequest>(() => ({ ...EMPTY, timeZoneId: defaultTimeZone() }));
  const [errors, setErrors] = useState<CreateTenantErrors>({});
  const [created, setCreated] = useState<PlatformTenantDto | null>(null);

  const create = useMutation({
    mutationFn: platformApi.create,
    onSuccess: (tenant) => {
      setCreated(tenant);
      void queryClient.invalidateQueries({ queryKey: platformKeys.tenants() });
    },
  });

  if (created !== null) {
    return <ProvisioningProgress initial={created} pollMs={pollMs} />;
  }

  const set = (key: keyof CreateTenantRequest) => (value: string) => setForm((current) => ({ ...current, [key]: value }));

  function next() {
    const found = step === 0 ? validateOrganization(form) : step === 1 ? validateFirstAdmin(form) : {};
    setErrors(found);
    if (Object.keys(found).length === 0) setStep((s) => Math.min(s + 1, STEPS.length - 1));
  }

  const submitError = create.error instanceof ApiError ? create.error : null;

  return (
    <section className="gh-panel flex flex-col gap-4" aria-label="Create tenant">
      <WizardSteps steps={STEPS} active={step} label="Create tenant steps" />
      {step === 0 && (
        <div className="grid gap-3 md:grid-cols-2">
          <Field
            id="tenantId"
            label="Tenant id"
            value={form.tenantId}
            error={errors.tenantId}
            onChange={set("tenantId")}
            hint="Permanent, e.g. acme"
          />
          <Field id="displayName" label="Display name" value={form.displayName} error={errors.displayName} onChange={set("displayName")} />
          <Field
            id="emailDomain"
            label="Email domain"
            value={form.emailDomain}
            error={errors.emailDomain}
            onChange={set("emailDomain")}
            hint="Sign-in routing domain, e.g. acme.com"
          />
          <Field id="timeZoneId" label="Time zone (IANA)" value={form.timeZoneId} error={errors.timeZoneId} onChange={set("timeZoneId")} />
        </div>
      )}
      {step === 1 && (
        <div className="grid gap-3 md:grid-cols-2">
          <Field
            id="adminEmail"
            label="Admin email"
            type="email"
            value={form.adminEmail}
            error={errors.adminEmail}
            onChange={set("adminEmail")}
            hint={`Must end with @${form.emailDomain}`}
          />
          <Field
            id="adminGivenName"
            label="Given name"
            value={form.adminGivenName}
            error={errors.adminGivenName}
            onChange={set("adminGivenName")}
          />
          <Field
            id="adminFamilyName"
            label="Family name"
            value={form.adminFamilyName}
            error={errors.adminFamilyName}
            onChange={set("adminFamilyName")}
          />
        </div>
      )}
      {step === 2 && (
        <dl className="grid grid-cols-[max-content_1fr] gap-x-6 gap-y-2 text-sm" data-testid="create-tenant-review">
          <dt>Tenant</dt>
          <dd>
            {form.displayName} ({form.tenantId})
          </dd>
          <dt>Email domain</dt>
          <dd>{form.emailDomain}</dd>
          <dt>Time zone</dt>
          <dd>{form.timeZoneId}</dd>
          <dt>First admin</dt>
          <dd>
            {form.adminGivenName} {form.adminFamilyName} &lt;{form.adminEmail}&gt;
          </dd>
        </dl>
      )}
      {submitError !== null && (
        <p role="alert" className="text-sm text-destructive">
          {submitError.status === 429 ? "Too many tenants created in a minute. Try again shortly." : submitError.detail}
          {submitError.code !== undefined && ` (${submitError.code})`}
        </p>
      )}
      <div className="flex gap-2">
        {step > 0 && (
          <Button type="button" variant="outline" onClick={() => setStep((s) => s - 1)} disabled={create.isPending}>
            Back
          </Button>
        )}
        {step < STEPS.length - 1 ? (
          <Button type="button" onClick={next}>
            Next
          </Button>
        ) : (
          <Button type="button" onClick={() => create.mutate({ ...form, adminEmail: form.adminEmail.trim() })} disabled={create.isPending}>
            {create.isPending ? "Creating…" : "Create tenant"}
          </Button>
        )}
      </div>
    </section>
  );
}
