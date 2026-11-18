"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useMe } from "@/lib/api/me";
import type { PersonDetailDto } from "@/lib/api/types";
import { peopleApi, peopleKeys } from "../api";
import { formatDate } from "../labels";
import { MagicLinkSection, RevokeEntitlement, SponsorshipActions, TerminateAction } from "./person-actions";
import { CardRow, IssueCardForm, PersonPhoto } from "./person-cards";

export interface PersonDetailProps {
  personId: string;
}

export function PersonDetail({ personId }: PersonDetailProps) {
  const detail = useQuery({ queryKey: peopleKeys.detail(personId), queryFn: () => peopleApi.detail(personId) });

  if (detail.isError) {
    return <p role="alert">Could not load this person.</p>;
  }
  if (detail.data === undefined) {
    return <p>Loading…</p>;
  }
  return <PersonDetailView person={detail.data} />;
}

export function PersonDetailView({ person }: { person: PersonDetailDto }) {
  const { summary } = person;
  const me = useMe();
  const isAdmin = me.data?.roles.includes("TenantAdmin") === true;
  const queryClient = useQueryClient();
  const refresh = () => queryClient.invalidateQueries({ queryKey: peopleKeys.all });
  const isLocal = summary.source === "Local" && summary.type !== "Employee";
  const active = summary.status === "Active";

  return (
    <Card data-testid="person-detail">
      <CardHeader>
        <PersonPhoto personId={summary.personId} name={summary.displayName} />
        <CardTitle>{summary.displayName}</CardTitle>
        <p className="text-sm text-muted-foreground">
          {summary.type} · {summary.email}
        </p>
      </CardHeader>
      <CardContent className="flex flex-col gap-4 text-sm">
        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
          <dt>Status</dt>
          <dd>
            <Badge variant={active ? "secondary" : "destructive"}>{summary.status}</Badge>
          </dd>
          <dt>Eligible</dt>
          <dd data-testid="person-eligibility">
            {formatDate(summary.eligibilityStart)} – {formatDate(summary.eligibilityEnd)}
          </dd>
          {summary.sponsor !== null ? (
            <>
              <dt>Sponsor</dt>
              <dd>{summary.sponsor.displayName}</dd>
            </>
          ) : null}
          {summary.manager !== null ? (
            <>
              <dt>Manager</dt>
              <dd>{summary.manager.displayName}</dd>
            </>
          ) : null}
        </dl>

        {person.canManage && isLocal && active ? <SponsorshipActions personId={summary.personId} onDone={refresh} /> : null}

        <section className="flex flex-col gap-2">
          <h3 className="font-semibold">Cards</h3>
          {person.cards.length === 0 ? <p>No cards.</p> : null}
          {person.cards.map((card) => (
            <CardRow
              key={card.cardId}
              card={card}
              canActivate={person.canActivateCards && active}
              canDeactivate={person.canDeactivateCards ?? person.canManage}
              onDone={refresh}
            />
          ))}
          {person.canIssueCards === true && active && person.cards.every((c) => c.state === "Revoked") ? (
            <IssueCardForm personId={summary.personId} onDone={refresh} />
          ) : null}
        </section>

        <section className="flex flex-col gap-2">
          <h3 className="font-semibold">Access</h3>
          {person.entitlements.length === 0 ? <p>No access.</p> : null}
          <ul className="flex flex-col gap-1">
            {person.entitlements.map((e) => (
              <li key={e.entitlementId} data-testid="person-entitlement" className="flex flex-wrap items-center gap-2">
                <span>
                  {e.areaName} · {e.status} · until {formatDate(e.effectiveEnd)}
                  {e.revokeFulfillmentStatuses.length > 0 ? ` · revoke: ${e.revokeFulfillmentStatuses.join(", ")}` : ""}
                </span>
                {e.status === "Approved" && e.revokeAction != null ? (
                  <RevokeEntitlement entitlementId={e.entitlementId} action={e.revokeAction} onDone={refresh} />
                ) : null}
              </li>
            ))}
          </ul>
        </section>

        {summary.type === "Guest" && person.canIssueMagicLink && active ? <MagicLinkSection personId={summary.personId} /> : null}
        {person.canTerminate === true && active ? <TerminateAction personId={summary.personId} onDone={refresh} /> : null}
        {isAdmin ? (
          <a
            className="self-start text-primary underline"
            href={`/api/proxy/v1/people/${summary.personId}/export`}
            download
            data-testid="person-export"
          >
            Export personal data (GDPR)
          </a>
        ) : null}
      </CardContent>
    </Card>
  );
}
