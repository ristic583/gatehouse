"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { ApiError } from "@/lib/api/client";
import type { LifecycleSettingsDto } from "@/lib/api/types";
import { lifecycleApi, lifecycleKeys, SETTINGS } from "../api";
import { RetentionSettings } from "./retention-settings";

const messageOf = (error: Error | null) => (error instanceof ApiError ? error.detail : error?.message);

/** Console tab "Lifecycle" (TenantAdmin): run HR sync and the lifecycle job now, edit the six lifecycle settings. */
export function LifecyclePanel() {
  const hrSync = useMutation({ mutationFn: lifecycleApi.hrSync });
  const run = useMutation({ mutationFn: lifecycleApi.run });
  const settings = useQuery({ queryKey: lifecycleKeys.settings, queryFn: lifecycleApi.settings });
  const hrError =
    hrSync.error instanceof ApiError && hrSync.error.code === "hr_sync.feed_unavailable"
      ? "No HR feed for this tenant"
      : messageOf(hrSync.error);

  return (
    <div className="grid gap-6 lg:grid-cols-2">
      <Card>
        <CardHeader>
          <CardTitle>Run now</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4 text-sm">
          <div className="flex flex-col gap-2">
            <Button className="self-start" disabled={hrSync.isPending} onClick={() => hrSync.mutate()}>
              Run HR sync
            </Button>
            {hrSync.data !== undefined ? (
              <p role="status" data-testid="hr-sync-result">
                {`${hrSync.data.created} created · ${hrSync.data.updated} updated · `}
                {`${hrSync.data.unchanged} unchanged · ${hrSync.data.skipped} skipped`}
              </p>
            ) : null}
            {hrError !== undefined ? <p role="alert">{hrError}</p> : null}
          </div>
          <div className="flex flex-col gap-2">
            <Button className="self-start" disabled={run.isPending} onClick={() => run.mutate()}>
              Run lifecycle now
            </Button>
            {run.data !== undefined ? (
              <p role="status" data-testid="lifecycle-result">
                {run.data.terminated} terminated · {run.data.expired} expired · {run.data.renewalsRequested} renewals requested
              </p>
            ) : null}
            {run.error !== null ? <p role="alert">{messageOf(run.error)}</p> : null}
          </div>
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle>Settings</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-3 text-sm">
          {settings.isError ? <p role="alert">Could not load the settings.</p> : null}
          {settings.data === undefined ? (
            settings.isError ? null : (
              <p>Loading…</p>
            )
          ) : (
            SETTINGS.map((s) => <SettingRow key={s.key} setting={s} value={settings.data[s.field]} />)
          )}
        </CardContent>
      </Card>
      <div className="lg:col-span-2">
        <RetentionSettings />
      </div>
    </div>
  );
}

function SettingRow({ setting, value }: { setting: (typeof SETTINGS)[number]; value: number }) {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState(String(value));
  const save = useMutation({
    mutationFn: (days: number) => lifecycleApi.setSetting(setting.key, days),
    onSuccess: (updated: LifecycleSettingsDto) => queryClient.setQueryData(lifecycleKeys.settings, updated),
  });
  const days = Number.parseInt(draft, 10);
  const valid = Number.isInteger(days) && days >= 1 && days <= 3650 && String(days) === draft.trim();

  return (
    <form
      data-testid="lifecycle-setting"
      className="flex flex-wrap items-center gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        if (valid) {
          save.mutate(days);
        }
      }}
    >
      <label className="min-w-[16rem]" htmlFor={setting.key}>
        {setting.label}
      </label>
      <Input id={setting.key} className="w-24" inputMode="numeric" value={draft} onChange={(e) => setDraft(e.target.value)} />
      <Button size="sm" type="submit" disabled={!valid || save.isPending || days === value}>
        Save
      </Button>
      {save.error !== null ? <span role="alert">{messageOf(save.error)}</span> : null}
    </form>
  );
}
