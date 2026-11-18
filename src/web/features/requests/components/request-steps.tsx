"use client";

import { ShieldCheck } from "lucide-react";
import { reasonText } from "@/features/map/access-state";
import type { AreaAccessDto, PersonSummaryDto, SecurityLevelDto } from "@/lib/api/types";
import { CHAIN_TEXT } from "../api";
import { addCalendarDays, formatAccessTime } from "../request-window";

interface WizardStepNavProps {
  names: readonly string[];
  step: number;
  disabled: boolean;
  onPick: (step: number) => void;
}

/** Numbered step list; earlier steps can be revisited. */
export function WizardStepNav({ names, step, disabled, onPick }: WizardStepNavProps) {
  return (
    <ol className="gh-wizard-steps" aria-label="Request steps">
      {names.map((name, i) => (
        <li key={name} aria-current={i === step ? "step" : undefined}>
          <button type="button" disabled={i > step || disabled} onClick={() => onPick(i)}>
            <span>{i + 1}</span>
            {name}
          </button>
        </li>
      ))}
    </ol>
  );
}

type Location = { siteName: string; floorName: string } | undefined;

interface WhoWhereStepProps {
  area: AreaAccessDto;
  location: Location;
  level: SecurityLevelDto | undefined;
  myName: string | undefined;
  candidates: readonly PersonSummaryDto[];
  subjectId: string | null;
  onSubject: (personId: string | null) => void;
}

/** Step 1: who needs access (self or a sponsored/managed person) and the space with its policy. */
export function WhoWhereStep({ area, location, level, myName, candidates, subjectId, onSubject }: WhoWhereStepProps) {
  return (
    <>
      <dl className="gh-review-grid">
        <div>
          <dt>Who needs access</dt>
          <dd>
            {candidates.length > 0 ? (
              <select
                aria-label="Who needs access"
                value={subjectId ?? ""}
                onChange={(e) => onSubject(e.target.value === "" ? null : e.target.value)}
              >
                <option value="">{myName ?? "Myself"} (myself)</option>
                {candidates.map((p) => (
                  <option key={p.personId} value={p.personId}>
                    {p.displayName} ({p.type})
                  </option>
                ))}
              </select>
            ) : (
              (myName ?? "Loading your profile…")
            )}
            <small>{subjectId === null ? "Your signed-in account" : "On behalf of a person you sponsor or manage"}</small>
          </dd>
        </div>
        <div>
          <dt>Space</dt>
          <dd>
            {area.name}
            <small>{location ? `${location.siteName} · ${location.floorName}` : area.code}</small>
          </dd>
        </div>
        <div>
          <dt>Security level</dt>
          <dd>{level?.name ?? "Loading…"}</dd>
        </div>
        <div>
          <dt>Approval path</dt>
          <dd>{level ? CHAIN_TEXT[level.approverChain] : "Loading…"}</dd>
        </div>
      </dl>
      <div className="gh-policy-note">
        <ShieldCheck size={18} />
        <p>
          {level ? `Up to ${level.maxDurationDays} days. ` : ""}Access starts only after the required approvals, access-system confirmation,
          and the requested start time.
        </p>
      </div>
      {area.state === "Unknown" && (
        <p className="gh-policy-note">
          Current access cannot be verified: {reasonText(area.reason, area.since)}. Submitting a request does not confirm that a door can be
          used.
        </p>
      )}
    </>
  );
}

interface WhenWhyStepProps {
  level: SecurityLevelDto | undefined;
  today: string;
  startDate: string;
  endDate: string;
  justification: string;
  validation: string | null;
  onChange: (change: { startDate?: string; endDate?: string; justification?: string }) => void;
}

/** Step 2: dates (with duration presets) and the justification. */
export function WhenWhyStep({ level, today, startDate, endDate, justification, validation, onChange }: WhenWhyStepProps) {
  return (
    <>
      <div className="gh-duration-presets" aria-label="Common access durations">
        {[
          { days: 1, label: "Today" },
          { days: 7, label: "7 days" },
          { days: 30, label: "30 days" },
        ].map((p) => (
          <button
            type="button"
            key={p.days}
            disabled={!level || p.days > level.maxDurationDays}
            onClick={() => onChange({ startDate: today, endDate: addCalendarDays(today, p.days - 1) })}
          >
            {p.label}
          </button>
        ))}
      </div>
      <div className="gh-date-grid">
        <label>
          Start date
          <input type="date" required value={startDate} min={today} onChange={(e) => onChange({ startDate: e.target.value })} />
        </label>
        <label>
          End date
          <input
            type="date"
            required
            value={endDate}
            min={startDate || today}
            max={level && startDate ? addCalendarDays(startDate, level.maxDurationDays - 1) : undefined}
            onChange={(e) => onChange({ endDate: e.target.value })}
          />
        </label>
      </div>
      <p className="gh-field-help">
        Dates use your device’s time zone ({Intl.DateTimeFormat().resolvedOptions().timeZone}). Today starts when you send. Access ends at
        midnight after the selected end date. Maximum: {level?.maxDurationDays} days.
      </p>
      <label htmlFor="request-justification">Justification</label>
      <textarea
        id="request-justification"
        required
        minLength={10}
        maxLength={500}
        rows={3}
        placeholder="What work will you do here, and why is this access needed?"
        value={justification}
        onChange={(e) => onChange({ justification: e.target.value })}
        aria-describedby="request-purpose-help"
      />
      <p id="request-purpose-help" className="gh-field-help">
        {justification.trim().length}/500 characters · At least 10 characters
      </p>
      {validation && (
        <p className="gh-validation-note" role="status">
          {validation}
        </p>
      )}
    </>
  );
}

interface ReviewStepProps {
  area: AreaAccessDto;
  location: Location;
  level: SecurityLevelDto | undefined;
  subjectName: string | undefined;
  startDate: string;
  today: string;
  window: { requestedStart: string; requestedEnd: string };
  justification: string;
}

/** Step 3: summary of the request before sending. */
export function ReviewStep({ area, location, level, subjectName, startDate, today, window, justification }: ReviewStepProps) {
  return (
    <>
      <dl className="gh-review-grid">
        <div>
          <dt>For</dt>
          <dd>{subjectName}</dd>
        </div>
        <div>
          <dt>Space</dt>
          <dd>
            {area.name}
            <small>{location ? `${location.siteName} · ${location.floorName}` : area.code}</small>
          </dd>
        </div>
        <div>
          <dt>Starts</dt>
          <dd>{startDate === today ? "Now, when you send the request" : formatAccessTime(window.requestedStart)}</dd>
        </div>
        <div>
          <dt>Ends (exclusive)</dt>
          <dd>{formatAccessTime(window.requestedEnd)}</dd>
        </div>
        <div>
          <dt>Purpose</dt>
          <dd>{justification.trim()}</dd>
        </div>
        <div>
          <dt>Required approvals</dt>
          <dd>{level ? CHAIN_TEXT[level.approverChain] : "Loading…"}</dd>
        </div>
      </dl>
      <div className="gh-policy-note">
        <ShieldCheck size={18} />
        <p>
          This sends a request for review. Approval is followed by confirmation from the access system. Your eligibility may shorten the
          window.
        </p>
      </div>
    </>
  );
}
