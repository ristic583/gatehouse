"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import type { AlarmDto } from "@/lib/api/types";
import { ALARM_KIND_META } from "../door-state";
import { AcknowledgeAlarmDialog } from "./acknowledge-alarm-dialog";
import { formatClock } from "./event-feed";

export interface AlarmPanelProps {
  alarms: readonly AlarmDto[];
  /** False for read-only viewers (tenant admins). */
  canAct: boolean;
}

export function AlarmPanel({ alarms, canAct }: AlarmPanelProps) {
  const [selected, setSelected] = useState<AlarmDto | null>(null);
  const sorted = [...alarms].sort((a, b) => b.raisedAt.localeCompare(a.raisedAt));

  return (
    <section aria-label="Open alarms" className="flex flex-col gap-2">
      <h2 className="text-sm font-semibold">Alarms ({sorted.length})</h2>
      {sorted.length === 0 ? <p className="text-sm text-muted-foreground">No open alarms.</p> : null}
      <ul className="flex flex-col gap-1 text-sm">
        {sorted.map((alarm) => {
          const meta = ALARM_KIND_META[alarm.kind];
          const Icon = meta.icon;
          return (
            <li
              key={alarm.alarmId}
              data-testid="alarm-row"
              data-kind={alarm.kind}
              className="flex items-center justify-between gap-2 rounded-md border-l-4 bg-muted/40 px-2 py-1"
              style={{ borderColor: `var(${meta.colorVar})` }}
            >
              <span className="flex items-center gap-1">
                <Icon aria-hidden className="size-4" style={{ color: `var(${meta.colorVar})` }} />
                <span className="font-medium">{meta.label}</span> · {alarm.doorName} · {alarm.areaName} · {formatClock(alarm.raisedAt)}
              </span>
              {canAct ? (
                <Button size="sm" variant="outline" onClick={() => setSelected(alarm)}>
                  Acknowledge
                </Button>
              ) : null}
            </li>
          );
        })}
      </ul>
      <AcknowledgeAlarmDialog alarm={selected} onClose={() => setSelected(null)} />
    </section>
  );
}
