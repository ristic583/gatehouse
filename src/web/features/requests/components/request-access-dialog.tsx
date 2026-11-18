"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { roomAction } from "@/features/map/room-action";
import { ApiError } from "@/lib/api/client";
import { useMe } from "@/lib/api/me";
import type { AreaAccessDto } from "@/lib/api/types";
import { CHAIN_TEXT, MY_REQUESTS_KEY, requestsApi } from "../api";
import { toDateInput, toRequestWindow } from "../date-window";
import { validateRequestWindow } from "../request-window";
import { AreaStateView } from "./area-state-view";
import { NoActiveCardHint } from "./no-active-card-hint";
import { ReviewStep, WhenWhyStep, WhoWhereStep, WizardStepNav } from "./request-steps";
import { useTerm } from "@/features/theming/theme";
import { peopleApi } from "@/features/people/api";

export interface RequestAccessDialogProps {
  area: AreaAccessDto | null;
  onClose: () => void;
  location?: { siteName: string; floorName: string };
}
export function RequestAccessDialog({ area, onClose, location }: RequestAccessDialogProps) {
  return (
    <Dialog
      open={area !== null}
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
    >
      <DialogContent>{area && <RequestAccessBody key={area.areaId} area={area} onClose={onClose} location={location} />}</DialogContent>
    </Dialog>
  );
}
const STEP_NAMES = ["Who and where", "When and why", "Review"];
function RequestAccessBody({
  area,
  onClose,
  location,
}: {
  area: AreaAccessDto;
  onClose: () => void;
  location?: RequestAccessDialogProps["location"];
}) {
  const pathname = usePathname();
  const areaTerm = useTerm("Area");
  const queryClient = useQueryClient();
  const details = useQuery({ queryKey: ["area", area.areaId], queryFn: () => requestsApi.area(area.areaId) });
  const me = useMe();
  const myId = me.data?.person?.personId;
  const level = details.data?.securityLevel;
  const showState = roomAction(area).kind !== "request";
  const canViewRoster =
    (me.data?.roles ?? []).includes("SecurityStaff") || (!!myId && (details.data?.owners ?? []).some((o) => o.personId === myId));
  const today = toDateInput(new Date());
  const [step, setStep] = useState(0);
  const [startDate, setStartDate] = useState(today);
  const [endDate, setEndDate] = useState(today);
  const [justification, setJustification] = useState("");
  const [subjectId, setSubjectId] = useState<string | null>(null);
  // Request on behalf (lab finding #8): people this user sponsors or manages; the API checks RequestAccessRequirement.
  const people = useQuery({
    queryKey: ["people", "on-behalf", myId],
    queryFn: () => peopleApi.list("", ""),
    enabled: !showState && !!myId,
  });
  const candidates = (people.data ?? []).filter(
    (p) => p.personId !== myId && p.status === "Active" && (p.sponsor?.personId === myId || p.manager?.personId === myId),
  );
  const subjectName = subjectId === null ? me.data?.person?.displayName : candidates.find((p) => p.personId === subjectId)?.displayName;
  const [error, setError] = useState<string | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    heading.current?.focus();
  }, [step]);
  const submit = useMutation({
    mutationFn: () =>
      requestsApi.create({
        personId: subjectId,
        areaId: area.areaId,
        justification: justification.trim(),
        ...toRequestWindow(startDate, endDate, new Date()),
      }),
    onSuccess: async () => {
      toast.success("Request sent. Follow its progress in My requests.");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["access-map"] }),
        queryClient.invalidateQueries({ queryKey: MY_REQUESTS_KEY }),
      ]);
      onClose();
    },
    onError: (e: unknown) =>
      setError(e instanceof ApiError ? e.detail : "The request could not be sent. Your entries are saved here; try again."),
  });
  const validation = level ? validateRequestWindow(startDate, endDate, justification, level.maxDurationDays, new Date()) : null;
  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (submit.isPending) return;
    setError(null);
    if (!level || !myId) {
      setError("Wait until your profile and this space’s policy are available.");
      return;
    }
    if (step === 0) {
      setStep(1);
      return;
    }
    const currentError = validateRequestWindow(startDate, endDate, justification, level.maxDurationDays, new Date());
    if (currentError) {
      setError(currentError);
      return;
    }
    if (step === 1) {
      setStep(2);
      return;
    }
    submit.mutate();
  }
  const path = pathname === "/design-preview" ? "/design-preview?view=requests&area=" : "/requests?area=";
  const window = validation === null && level ? toRequestWindow(startDate, endDate, new Date()) : null;
  return (
    <>
      <DialogHeader>
        <DialogTitle>
          <span className="mr-2 text-xs font-normal text-muted-foreground" data-testid="area-term">
            {areaTerm}
          </span>
          {area.name}
        </DialogTitle>
        <DialogDescription>
          {level
            ? `${level.name} security · approval: ${CHAIN_TEXT[level.approverChain]} · max ${level.maxDurationDays} days`
            : "Loading access policy…"}
        </DialogDescription>
      </DialogHeader>
      {details.isError && (
        <p role="alert">
          Could not load this space’s policy. <button onClick={() => void details.refetch()}>Retry</button>
        </p>
      )}
      {me.isError && (
        <p role="alert">
          Could not load your profile. <button onClick={() => void me.refetch()}>Retry</button>
        </p>
      )}
      {me.data && !me.data.person && (
        <p role="alert">Your account is not linked to a person yet. Contact your administrator before requesting access.</p>
      )}
      {showState ? (
        <AreaStateView area={area} myId={myId} path={path} onClose={onClose} />
      ) : (
        <form onSubmit={onSubmit} className="gh-request-form gh-request-wizard" aria-label="Request access">
          <WizardStepNav
            names={STEP_NAMES}
            step={step}
            disabled={submit.isPending}
            onPick={(i) => {
              setStep(i);
              setError(null);
            }}
          />
          <h3 ref={heading} tabIndex={-1} className="gh-wizard-heading">
            {STEP_NAMES[step]}
          </h3>
          {step === 0 && (
            <WhoWhereStep
              area={area}
              location={location}
              level={level}
              myName={me.data?.person?.displayName}
              candidates={candidates}
              subjectId={subjectId}
              onSubject={(personId) => {
                setSubjectId(personId);
                setError(null);
              }}
            />
          )}
          {step === 1 && (
            <WhenWhyStep
              level={level}
              today={today}
              startDate={startDate}
              endDate={endDate}
              justification={justification}
              validation={validation}
              onChange={(change) => {
                if (change.startDate !== undefined) setStartDate(change.startDate);
                if (change.endDate !== undefined) setEndDate(change.endDate);
                if (change.justification !== undefined) setJustification(change.justification);
                setError(null);
              }}
            />
          )}
          {step === 2 && window && (
            <ReviewStep
              area={area}
              location={location}
              level={level}
              subjectName={subjectName}
              startDate={startDate}
              today={today}
              window={window}
              justification={justification}
            />
          )}
          {myId && <NoActiveCardHint personId={subjectId} />}
          {step === 2 && validation && <p role="alert">{validation} Go back to update your request.</p>}
          {error && (
            <p role="alert" className="text-destructive">
              {error}
            </p>
          )}
          <div className="gh-wizard-actions">
            <Button
              type="button"
              variant="outline"
              disabled={submit.isPending}
              onClick={() => {
                if (step === 0) onClose();
                else {
                  setStep(step - 1);
                  setError(null);
                }
              }}
            >
              {step === 0 ? "Cancel" : "Back"}
            </Button>
            <Button
              type="submit"
              disabled={submit.isPending || !level || !myId || details.isError || me.isError || (step > 0 && validation !== null)}
            >
              {submit.isPending ? "Sending…" : step === 0 ? "Continue" : step === 1 ? "Review request" : "Send request"}
            </Button>
          </div>
        </form>
      )}
      {canViewRoster && (
        <Link href={`/areas/${area.areaId}/roster`} className="text-sm underline">
          View roster
        </Link>
      )}
    </>
  );
}
