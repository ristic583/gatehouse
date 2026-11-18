"use client";

import { useIsMutating, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { WizardSteps } from "@/features/platform/components/wizard-steps";
import { ApiError } from "@/lib/api/client";
import type { OnboardingStatusDto, OnboardingStep, OnboardingStepDto } from "@/lib/api/types";
import { onboardingApi, onboardingKeys, STEP_LABEL, STEP_ORDER } from "../api";
import { EMBEDDED_STEPS, OnboardingStepPanel } from "./onboarding-step-panel";

const STATE_TEXT = { NotStarted: "Not started", InProgress: "In progress", Complete: "Complete", Skipped: "Skipped" } as const;
const done = (s: OnboardingStepDto) => s.state === "Complete" || s.state === "Skipped";

export interface OnboardingFlowViewProps {
  tenantName: string;
  status: OnboardingStatusDto;
  onSkip: (step: OnboardingStep) => void;
  onFinish: () => void;
  busy?: boolean;
}

/** Server state only (`GET /api/v1/admin/onboarding`); identity, people, PACS and connector panels embed their admin form. */
export function OnboardingFlowView({ tenantName, status, onSkip, onFinish, busy }: OnboardingFlowViewProps) {
  const steps = STEP_ORDER.map((step) => status.steps.find((s) => s.step === step)).filter((s): s is OnboardingStepDto => s !== undefined);
  const firstOpen = steps.findIndex((s) => !done(s));
  const active = firstOpen === -1 ? steps.length : firstOpen;
  const [reviewing, setReviewing] = useState(false);
  // undefined = follow the active step; the user can open another embedded panel or close it (one panel at a time).
  const [chosen, setChosen] = useState<OnboardingStep | null | undefined>(undefined);
  const activeStep = steps[active]?.step;
  const openStep = chosen === undefined ? (activeStep !== undefined && EMBEDDED_STEPS.has(activeStep) ? activeStep : null) : chosen;

  return (
    <div className="flex flex-col gap-4">
      <section className="gh-panel" aria-label="Welcome" data-testid="onboarding-welcome">
        <p className="gh-eyebrow">WELCOME</p>
        <h2 className="text-xl font-medium">Set up {tenantName}</h2>
        <p className="text-sm text-muted-foreground">
          Connect sign-in, model your buildings, add people, register your access control system and its connector. You can leave and come
          back at any time.
        </p>
      </section>
      <WizardSteps
        steps={[...steps.map((s) => STEP_LABEL[s.step]), "Review"]}
        active={reviewing ? steps.length : active}
        label="Onboarding steps"
      />
      <ol className="flex flex-col gap-3">
        {steps.map((s, i) => (
          <li
            key={s.step}
            className={`gh-panel flex flex-col gap-2 ${i === active && !reviewing ? "ring-2 ring-primary" : ""}`}
            data-testid={`onboarding-step-${s.step}`}
            data-state={s.state}
          >
            <div className="flex items-center gap-2">
              <h3 className="font-medium">
                {i + 1}. {STEP_LABEL[s.step]}
              </h3>
              <Badge variant={s.state === "Complete" ? "default" : s.state === "Skipped" ? "secondary" : "outline"}>
                {STATE_TEXT[s.state]}
              </Badge>
            </div>
            <p className="text-sm">{s.detail}</p>
            <div className="flex flex-wrap gap-2">
              {EMBEDDED_STEPS.has(s.step) ? (
                <Button
                  type="button"
                  size="sm"
                  variant={openStep === s.step ? "outline" : "default"}
                  aria-expanded={openStep === s.step}
                  onClick={() => setChosen(openStep === s.step ? null : s.step)}
                >
                  {openStep === s.step ? "Hide" : s.state === "Complete" ? "Review" : "Open"} {STEP_LABEL[s.step]}
                </Button>
              ) : (
                <Link className="gh-primary-link" href={s.href}>
                  {s.state === "Complete" ? "Review" : "Open"} {STEP_LABEL[s.step]}
                </Link>
              )}
              {s.skippable && !done(s) && (
                <Button type="button" variant="outline" size="sm" disabled={busy} onClick={() => onSkip(s.step)}>
                  Skip {STEP_LABEL[s.step]}
                </Button>
              )}
            </div>
            {openStep === s.step && (
              <div data-testid={`onboarding-panel-${s.step}`} className="flex flex-col gap-3">
                <OnboardingStepPanel step={s.step} />
              </div>
            )}
          </li>
        ))}
      </ol>
      <section className="gh-panel flex flex-col gap-2" aria-label="Review" data-testid="onboarding-review">
        <h3 className="font-medium">{steps.length + 1}. Review</h3>
        <ul className="text-sm">
          {steps.map((s) => (
            <li key={s.step}>
              {STEP_LABEL[s.step]}: {STATE_TEXT[s.state]}
            </li>
          ))}
        </ul>
        {!status.canComplete && (
          <p className="text-sm text-muted-foreground">Finish the required steps first. Only Connector and Staff can be skipped.</p>
        )}
        <Button
          type="button"
          className="self-start"
          disabled={!status.canComplete || busy}
          onFocus={() => setReviewing(true)}
          onBlur={() => setReviewing(false)}
          onClick={onFinish}
        >
          Finish onboarding
        </Button>
      </section>
    </div>
  );
}

export function OnboardingFlow({ tenantName }: { tenantName: string }) {
  const queryClient = useQueryClient();
  const router = useRouter();
  // Some embedded forms save without a tracked mutation, so the status is also re-read every 15 s while the page is open.
  const status = useQuery({
    queryKey: onboardingKeys.status,
    queryFn: onboardingApi.status,
    refetchOnWindowFocus: true,
    refetchInterval: 15_000,
    staleTime: 0,
  });
  // Embedded forms change server state; re-read the status whenever their mutations settle instead of guessing it here.
  const mutating = useIsMutating();
  const wasMutating = useRef(false);
  useEffect(() => {
    if (wasMutating.current && mutating === 0) void queryClient.invalidateQueries({ queryKey: onboardingKeys.status });
    wasMutating.current = mutating > 0;
  }, [mutating, queryClient]);
  const onError = (e: Error) => toast.error(e instanceof ApiError ? e.detail : "The action failed.");
  const skip = useMutation({
    mutationFn: onboardingApi.skip,
    onSuccess: (next) => queryClient.setQueryData(onboardingKeys.status, next),
    onSettled: () => queryClient.invalidateQueries({ queryKey: onboardingKeys.status }),
    onError,
  });
  const complete = useMutation({
    mutationFn: onboardingApi.complete,
    onSuccess: async () => {
      toast.success("Onboarding complete");
      await queryClient.invalidateQueries({ queryKey: onboardingKeys.status });
      router.push("/dashboard");
      router.refresh();
    },
    onError,
  });

  if (status.isPending) return <p className="gh-loading">Loading onboarding…</p>;
  if (status.isError) return <p role="alert">Onboarding status could not be loaded.</p>;
  if (status.data.completedAt !== null) {
    return (
      <section className="gh-panel" data-testid="onboarding-complete">
        <p>Onboarding was completed {new Date(status.data.completedAt).toLocaleString()}.</p>
        <Link className="gh-primary-link" href="/dashboard">
          Go to the dashboard
        </Link>
      </section>
    );
  }
  return (
    <OnboardingFlowView
      tenantName={tenantName}
      status={status.data}
      busy={skip.isPending || complete.isPending}
      onSkip={(step) => skip.mutate(step)}
      onFinish={() => complete.mutate()}
    />
  );
}
