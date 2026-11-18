"use client";

import { ApiError } from "@/lib/api/client";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { FULFILLMENTS_KEY, fulfillmentApi, type FulfillmentView } from "../api";

export interface FulfillmentQueueProps {
  onSelect: (id: string) => void;
}

export function FulfillmentQueue({ onSelect }: FulfillmentQueueProps) {
  const [view, setView] = useState<FulfillmentView>("open");
  const query = useQuery({
    queryKey: [...FULFILLMENTS_KEY, view],
    queryFn: () => fulfillmentApi.list(view),
    refetchInterval: 10_000,
  });

  return (
    <div className="flex flex-col gap-3">
      <div className="flex gap-2">
        <Button size="sm" variant={view === "open" ? "default" : "outline"} aria-pressed={view === "open"} onClick={() => setView("open")}>
          Open
        </Button>
        <Button size="sm" variant={view === "done" ? "default" : "outline"} aria-pressed={view === "done"} onClick={() => setView("done")}>
          Done
        </Button>
      </div>
      {query.isError ? (
        <p role="alert">
          {query.error instanceof ApiError && query.error.status === 403
            ? "You are not permitted to see the fulfillment queue."
            : "Could not load the fulfillment queue."}
        </p>
      ) : null}
      {query.data === undefined ? (
        query.isError ? null : (
          <p>Loading…</p>
        )
      ) : query.data.length === 0 ? (
        <p>No fulfillment work waiting.</p>
      ) : (
        <table className="w-full text-left text-sm">
          <thead>
            <tr>
              <th>Priority</th>
              <th>Kind</th>
              <th>Person</th>
              <th>Area</th>
              <th>PACS</th>
              <th>Status</th>
              <th>Rev</th>
              <th>Updated</th>
            </tr>
          </thead>
          <tbody>
            {query.data.map((item) => (
              <tr
                key={item.fulfillmentId}
                data-testid={`fulfillment-${item.fulfillmentId}`}
                className="cursor-pointer border-t hover:bg-muted"
                onClick={() => onSelect(item.fulfillmentId)}
              >
                <td>{item.priority === "High" ? <Badge variant="destructive">Emergency</Badge> : "Normal"}</td>
                <td>{item.kind}</td>
                <td>{item.subject.displayName}</td>
                <td>
                  {item.area.name} · {item.area.siteName}
                </td>
                <td>
                  {item.pacsInstanceName} · {item.vendorAccessLevelRef}
                </td>
                <td>
                  <Badge variant="secondary">{item.status}</Badge>
                </td>
                <td>{item.revision}</td>
                <td>{new Date(item.updatedAt).toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
