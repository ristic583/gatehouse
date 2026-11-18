"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { ApiError } from "@/lib/api/client";
import { RETENTION_SETTINGS, retentionApi, retentionKeys } from "../retention";

const messageOf = (error: Error | null) => (error instanceof ApiError ? error.detail : error?.message);

/** GDPR retention (TenantAdmin): three settings and "Run retention now". Audit entries are never deleted. */
export function RetentionSettings() {
  const settings = useQuery({ queryKey: retentionKeys.settings, queryFn: retentionApi.settings });
  const run = useMutation({ mutationFn: retentionApi.run });

  return (
    <Card>
      <CardHeader>
        <CardTitle>Data retention (GDPR)</CardTitle>
        <p className="text-sm text-muted-foreground">
          Audit entries are kept forever; anonymization keeps ids so the audit chain stays intact.
        </p>
      </CardHeader>
      <CardContent className="flex flex-col gap-3 text-sm">
        {settings.isError ? <p role="alert">Could not load the retention settings.</p> : null}
        {settings.data === undefined ? (
          settings.isError ? null : (
            <p>Loading…</p>
          )
        ) : (
          RETENTION_SETTINGS.map((s) => <RetentionRow key={s.key} setting={s} value={settings.data[s.field]} />)
        )}
        <Button className="self-start" variant="outline" disabled={run.isPending} onClick={() => run.mutate()}>
          Run retention now
        </Button>
        {run.data !== undefined ? (
          <p role="status" data-testid="retention-result">
            {run.data.photosDeleted} photos deleted · {run.data.peopleAnonymized} people anonymized
          </p>
        ) : null}
        {run.error !== null ? <p role="alert">{messageOf(run.error)}</p> : null}
      </CardContent>
    </Card>
  );
}

function RetentionRow({ setting, value }: { setting: (typeof RETENTION_SETTINGS)[number]; value: number }) {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState(String(value));
  const save = useMutation({
    mutationFn: (days: number) => retentionApi.set(setting.key, days),
    onSuccess: (updated) => queryClient.setQueryData(retentionKeys.settings, updated),
  });
  const days = Number.parseInt(draft, 10);
  const valid = String(days) === draft.trim() && days >= setting.min && days <= setting.max;

  return (
    <form
      data-testid="retention-setting"
      className="flex flex-wrap items-center gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        if (valid) {
          save.mutate(days);
        }
      }}
    >
      <label className="min-w-[18rem]" htmlFor={setting.key}>
        {setting.label}
      </label>
      <Input id={setting.key} className="w-24" inputMode="numeric" value={draft} onChange={(e) => setDraft(e.target.value)} />
      <Button size="sm" type="submit" disabled={!valid || save.isPending || days === value}>
        Save
      </Button>
      {!valid ? <span role="alert">{`${setting.min}-${setting.max} days`}</span> : null}
      {save.error !== null ? <span role="alert">{messageOf(save.error)}</span> : null}
    </form>
  );
}
