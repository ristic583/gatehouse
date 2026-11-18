"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { peopleApi, peopleKeys } from "../api";
import { formatDate, PERSON_TYPES } from "../labels";
import { OnboardDialog } from "./onboard-dialog";

export interface PeopleListProps {
  selectedId: string | null;
  onSelect: (personId: string) => void;
}

export function PeopleList({ selectedId, onSelect }: PeopleListProps) {
  const [query, setQuery] = useState("");
  const [type, setType] = useState("");
  const [onboarding, setOnboarding] = useState(false);
  const people = useQuery({ queryKey: peopleKeys.list(query, type), queryFn: () => peopleApi.list(query, type) });

  return (
    <section className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <Input
          aria-label="Search people"
          placeholder="Search name or email"
          className="w-full min-w-0 sm:max-w-xs"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
        <select
          aria-label="Type filter"
          className="rounded border px-2 py-1 text-sm"
          value={type}
          onChange={(e) => setType(e.target.value)}
        >
          <option value="">All types</option>
          {PERSON_TYPES.map((t) => (
            <option key={t} value={t}>
              {t}
            </option>
          ))}
        </select>
        <Button className="ml-auto" onClick={() => setOnboarding(true)}>
          Add person
        </Button>
      </div>
      {onboarding ? (
        <OnboardDialog
          onClose={() => setOnboarding(false)}
          onCreated={(person) => {
            setOnboarding(false);
            onSelect(person.summary.personId);
          }}
        />
      ) : null}
      {people.isError ? <p role="alert">Could not load people.</p> : null}
      {people.data === undefined ? (
        people.isError ? null : (
          <p>Loading…</p>
        )
      ) : people.data.length === 0 ? (
        <p>No people to show.</p>
      ) : (
        <table className="w-full table-fixed text-left text-sm sm:table-auto">
          <thead>
            <tr>
              <th>Name</th>
              <th className="hidden sm:table-cell">Type</th>
              <th className="w-24 sm:w-auto">Status</th>
              <th className="hidden sm:table-cell">Eligible until</th>
            </tr>
          </thead>
          <tbody>
            {people.data.map((person) => (
              <tr
                key={person.personId}
                data-testid="person-row"
                aria-selected={person.personId === selectedId}
                className="cursor-pointer border-t hover:bg-muted"
                onClick={() => onSelect(person.personId)}
              >
                <td className="min-w-0">
                  <span className="break-words">{person.displayName}</span>
                  <div className="truncate text-xs text-muted-foreground">
                    <span className="sm:hidden">{person.type} · </span>
                    {person.email}
                  </div>
                </td>
                <td className="hidden sm:table-cell">{person.type}</td>
                <td>
                  <Badge variant={person.status === "Active" ? "secondary" : "destructive"}>{person.status}</Badge>
                </td>
                <td className="hidden sm:table-cell">{formatDate(person.eligibilityEnd)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
