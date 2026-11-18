"use client";

import { useQuery } from "@tanstack/react-query";
import { IdentityProviderPanel } from "@/features/admin/identity/components/identity-provider-form";
import { pacsAdminApi, pacsAdminKeys } from "@/features/admin/pacs/api";
import { PacsAdmin, PacsInstanceDetail } from "@/features/admin/pacs/components/pacs-admin";
import { PeopleImport } from "@/features/admin/people/components/people-import";
import { PageState, QueryErrorState } from "@/components/ui/page-state";
import type { OnboardingStep } from "@/lib/api/types";

/** Steps whose admin form is embedded in the onboarding panel (lab #23); Sites and Staff keep their full pages. */
export const EMBEDDED_STEPS: ReadonlySet<OnboardingStep> = new Set<OnboardingStep>(["IdentityProvider", "People", "Pacs", "Connector"]);

/** PACS mapping and connector enrollment: create the first instance here, then map and enroll it in place. */
function OnboardingPacsPanel() {
  const list = useQuery({ queryKey: pacsAdminKeys.list, queryFn: pacsAdminApi.list });
  if (list.isPending) return <PageState kind="loading" message="Loading PACS instances…" />;
  if (list.isError) {
    return <QueryErrorState error={list.error} message="PACS instances could not be loaded." onRetry={() => list.refetch()} />;
  }
  const first = list.data[0];
  return first === undefined ? <PacsAdmin /> : <PacsInstanceDetail id={first.id} />;
}

/** The admin form for one onboarding step; the step's state still comes only from the server status. */
export function OnboardingStepPanel({ step }: { step: OnboardingStep }) {
  switch (step) {
    case "IdentityProvider":
      return <IdentityProviderPanel />;
    case "People":
      return <PeopleImport />;
    case "Pacs":
    case "Connector":
      return <OnboardingPacsPanel />;
    default:
      return null;
  }
}
