"use client";

import { useQuery } from "@tanstack/react-query";
import { useReducedMotion } from "framer-motion";
import { ChevronLeft, ChevronRight, Pause, Play } from "lucide-react";
import dynamic from "next/dynamic";
import { Component, type ReactNode, useCallback, useEffect, useState } from "react";
import { createLiveConnection } from "@/features/live/hooks/use-live-site";
import type { LiveConnectionFactory } from "@/features/live/types";
import { apiClient } from "@/lib/api/client";
import { useMe } from "@/lib/api/me";
import type { LiveUpdateDto, SiteDto } from "@/lib/api/types";
import { DOOR_LANE, type FlowStep, GOVERNANCE_LANE, SIGN_IN_NOTE } from "../flows";
import type { Track } from "../scene/story";

const BuildingScene = dynamic(() => import("../scene/building-scene"), {
  ssr: false,
  loading: () => <div className="flex h-full items-center justify-center text-sm text-muted-foreground">Loading the 3D building…</div>,
});

const LANES: Record<Track, readonly FlowStep[]> = {
  governance: GOVERNANCE_LANE,
  door: DOOR_LANE,
};
const TRACK_TITLE: Record<Track, string> = {
  governance: "Getting access: request → approve → apply → confirm",
  door: "At the door: the controller decides, Gatehouse observes",
};

class SceneBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  override state = { failed: false };
  static getDerivedStateFromError() {
    return { failed: true };
  }
  override render() {
    return this.state.failed ? (
      <div className="flex h-full items-center justify-center p-6 text-center text-sm text-muted-foreground">
        The 3D view needs WebGL. The steps below describe the same flow.
      </div>
    ) : (
      this.props.children
    );
  }
}

function Lane({
  track,
  current,
  step,
  onPick,
  pulse,
}: {
  track: Track;
  current: Track;
  step: number;
  onPick: (track: Track, step: number) => void;
  pulse?: number;
}) {
  const steps = LANES[track];
  return (
    <section className="flex flex-col gap-2" data-testid={track === "governance" ? "lane-governance" : "lane-door"} data-pulse={pulse}>
      <h2 className="text-sm font-semibold">{TRACK_TITLE[track]}</h2>
      <ol className="flex flex-wrap gap-1.5">
        {steps.map((s, i) => {
          const active = track === current && i === step;
          return (
            <li key={s.id}>
              <button
                type="button"
                data-testid={`step-${s.id}`}
                aria-current={active ? "step" : undefined}
                onClick={() => onPick(track, i)}
                className={`rounded-full border px-3 py-1 text-xs transition-colors ${
                  active ? "border-sky-500 bg-sky-500 text-white" : "hover:bg-muted"
                }`}
              >
                {i + 1}. {s.title}
              </button>
            </li>
          );
        })}
      </ol>
    </section>
  );
}

/** 3D explainer of the real architecture. Staff can attach a site: a real door event replays the door track. */
export function HowItWorksView({ connectionFactory }: { connectionFactory?: LiveConnectionFactory }) {
  const me = useMe();
  const isStaff = me.data?.roles.some((r) => r === "SecurityStaff" || r === "TenantAdmin") === true;
  const sites = useQuery({
    queryKey: ["sites"],
    queryFn: () => apiClient.get<SiteDto[]>("/v1/sites"),
    enabled: isStaff,
  });
  const [siteId, setSiteId] = useState<string | null>(null);
  const [realEvents, setRealEvents] = useState(0);
  const [track, setTrack] = useState<Track>("governance");
  const [step, setStep] = useState(0);
  const reducedMotion = useReducedMotion();
  const [userPlaying, setUserPlaying] = useState<boolean | null>(null);
  const playing = userPlaying ?? reducedMotion !== true;
  const [restartKey, setRestartKey] = useState(0);

  useEffect(() => {
    if (siteId === null) {
      return undefined;
    }
    const connection = (connectionFactory ?? createLiveConnection)();
    let disposed = false;
    connection.on("live", (update: LiveUpdateDto) => {
      if (!disposed && update.kind === "DoorEvent") {
        setRealEvents((n) => n + 1);
        setTrack("door");
        setStep(0);
        setRestartKey((k) => k + 1);
      }
    });
    void connection
      .start()
      .then(() => connection.invoke("JoinSite", siteId))
      .catch(() => undefined);
    return () => {
      disposed = true;
      void connection.stop();
    };
  }, [siteId, connectionFactory]);

  const pick = useCallback((t: Track, i: number) => {
    setTrack(t);
    setStep(i);
    setRestartKey((k) => k + 1);
  }, []);

  const next = useCallback(() => {
    if (step + 1 < LANES[track].length) {
      pick(track, step + 1);
    } else {
      pick(track === "governance" ? "door" : "governance", 0);
    }
  }, [pick, step, track]);

  const prev = () =>
    step > 0
      ? pick(track, step - 1)
      : pick(track === "governance" ? "door" : "governance", LANES[track === "governance" ? "door" : "governance"].length - 1);
  const current = LANES[track][step];

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">
        One office floor and the Gatehouse services above it. Every step names the component that really does it; drag to look around.
      </p>
      <div className="relative h-[min(72vh,680px)] min-h-[420px] overflow-hidden rounded-xl border bg-[#e9eef3]">
        <SceneBoundary>
          <BuildingScene track={track} step={step} playing={playing} restartKey={restartKey} onStepEnd={next} />
        </SceneBoundary>
        {current !== undefined ? (
          <div
            className="absolute inset-x-3 bottom-3 rounded-lg border bg-background/92 p-3 shadow-lg backdrop-blur sm:right-auto sm:max-w-md"
            data-testid="step-card"
          >
            <div className="text-xs text-muted-foreground">
              {track === "governance" ? "Getting access" : "At the door"} · step {step + 1} of {LANES[track].length}
            </div>
            <div className="font-semibold">{current.title}</div>
            <div className="font-mono text-xs">{current.component}</div>
            <div className="mt-1 text-sm text-muted-foreground">{current.detail}</div>
            <div className="mt-2 flex items-center gap-1">
              <button type="button" aria-label="Previous step" onClick={prev} className="rounded-md border p-1.5 hover:bg-muted">
                <ChevronLeft className="size-4" />
              </button>
              <button
                type="button"
                aria-label={playing ? "Pause" : "Play"}
                onClick={() => setUserPlaying(!playing)}
                className="rounded-md border p-1.5 hover:bg-muted"
              >
                {playing ? <Pause className="size-4" /> : <Play className="size-4" />}
              </button>
              <button type="button" aria-label="Next step" onClick={next} className="rounded-md border p-1.5 hover:bg-muted">
                <ChevronRight className="size-4" />
              </button>
            </div>
          </div>
        ) : null}
      </div>
      <Lane track="governance" current={track} step={step} onPick={pick} />
      <Lane track="door" current={track} step={step} onPick={pick} pulse={realEvents} />
      <aside className="rounded-lg border border-dashed p-3 text-sm" data-testid="sign-in-note">
        <div className="font-medium">{SIGN_IN_NOTE.title}</div>
        <div className="font-mono text-xs">{SIGN_IN_NOTE.component}</div>
        <div className="text-muted-foreground">{SIGN_IN_NOTE.detail}</div>
      </aside>
      {isStaff ? (
        <label className="flex flex-wrap items-center gap-2 text-sm">
          Replay the door track on real events from
          <select
            aria-label="Live site"
            className="rounded border px-2 py-1"
            value={siteId ?? ""}
            onChange={(e) => setSiteId(e.target.value === "" ? null : e.target.value)}
          >
            <option value="">(animation only)</option>
            {(sites.data ?? []).map((s) => (
              <option key={s.siteId} value={s.siteId}>
                {s.name}
              </option>
            ))}
          </select>
          {siteId !== null ? (
            <span data-testid="lane-door-pulse" data-count={realEvents}>
              {realEvents} real door events
            </span>
          ) : null}
        </label>
      ) : null}
    </div>
  );
}
