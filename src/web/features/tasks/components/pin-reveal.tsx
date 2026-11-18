"use client";

import { useMutation } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api/client";
import { tasksApi } from "../api";

const MASK = "••••";

/**
 * "Show PIN" for the claimant of an InProgress PinReveal task. The digits live only in this component's state, are replaced by
 * a mask after `maskAfterSeconds` (30 s) and are cleared on unmount; the mutation result is not cached.
 */
export function PinReveal({ taskId }: { taskId: string }) {
  const [pin, setPin] = useState<string | null>(null);
  const [secondsLeft, setSecondsLeft] = useState(0);
  const [masked, setMasked] = useState(false);
  const maskAfter = useRef(30);
  const reveal = useMutation({
    mutationFn: () => tasksApi.revealPin(taskId),
    gcTime: 0,
    onSuccess: (result) => {
      setMasked(false);
      maskAfter.current = result.maskAfterSeconds;
      setPin(result.pin);
      setSecondsLeft(result.maskAfterSeconds);
    },
  });
  const { reset } = reveal;

  useEffect(() => {
    if (pin === null) {
      return undefined;
    }
    let remaining = maskAfter.current;
    const timer = setInterval(() => {
      remaining -= 1;
      if (remaining > 0) {
        setSecondsLeft(remaining);
        return;
      }
      clearInterval(timer);
      setPin(null);
      setMasked(true);
      reset();
    }, 1_000);
    return () => clearInterval(timer);
  }, [pin, reset]);

  useEffect(
    () => () => {
      setPin(null);
      reset();
    },
    [reset],
  );

  const error = reveal.error instanceof ApiError ? reveal.error.detail : reveal.error?.message;

  return (
    <div className="flex flex-wrap items-center gap-2">
      {pin === null ? (
        <>
          <Button size="sm" data-testid="pin-reveal" disabled={reveal.isPending} onClick={() => reveal.mutate()}>
            Show PIN
          </Button>
          {masked ? (
            <span data-testid="pin-value" className="font-mono">
              {MASK}
            </span>
          ) : null}
        </>
      ) : (
        <>
          <span data-testid="pin-value" className="font-mono text-lg tracking-widest">
            {pin}
          </span>
          <span className="text-muted-foreground">masked in {secondsLeft} s</span>
        </>
      )}
      {error !== undefined ? (
        <p role="alert" className="w-full text-destructive">
          {error}
        </p>
      ) : null}
    </div>
  );
}
