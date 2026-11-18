"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api/client";
import type { PeopleImportResultDto } from "@/lib/api/types";
import { peopleAdminApi, peopleAdminError } from "../api";

export interface PeopleImportProps {
  importCsv?: (file: File, dryRun: boolean) => Promise<PeopleImportResultDto>;
}

/** CSV import (`email,given_name,family_name[,manager_email]`): dry run first, Import only after an error-free preview. */
export function PeopleImport({ importCsv = peopleAdminApi.importCsv }: PeopleImportProps) {
  const queryClient = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<PeopleImportResultDto | null>(null);
  const [done, setDone] = useState<PeopleImportResultDto | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run(dryRun: boolean) {
    if (file === null) return;
    setBusy(true);
    setError(null);
    try {
      const result = await importCsv(file, dryRun);
      if (dryRun) {
        setPreview(result);
      } else {
        setDone(result);
        setPreview(null);
        toast.success("Import finished");
        await queryClient.invalidateQueries({ queryKey: ["people"] });
      }
    } catch (e) {
      setError(e instanceof ApiError ? peopleAdminError(e.code, e.detail) : "The import failed.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="gh-panel flex flex-col gap-3" aria-label="Import employees">
      <h3 className="gh-panel-title">Import employees (CSV)</h3>
      <p className="text-xs text-muted-foreground">
        Columns: email, given_name, family_name, optional manager_email. Existing employees are matched by email.
      </p>
      <div className="flex flex-col gap-1">
        <Label htmlFor="people-import-file">CSV file</Label>
        <input
          id="people-import-file"
          type="file"
          accept=".csv,text/csv"
          onChange={(e) => {
            setFile(e.target.files?.[0] ?? null);
            setPreview(null);
            setDone(null);
          }}
        />
      </div>
      <div className="flex gap-2">
        <Button type="button" variant="outline" disabled={file === null || busy} onClick={() => void run(true)}>
          Preview
        </Button>
        <Button type="button" disabled={preview === null || preview.errors.length > 0 || busy} onClick={() => void run(false)}>
          Import
        </Button>
      </div>
      {preview !== null && (
        <div data-testid="import-preview" className="flex flex-col gap-1 text-sm">
          <p>
            Dry run: {preview.created} new · {preview.updated} updated · {preview.unchanged} unchanged · {preview.errors.length} errors
          </p>
          {preview.errors.length > 0 && (
            <ul className="flex flex-col gap-1">
              {preview.errors.map((e) => (
                <li key={`${e.line}-${e.code}-${e.field ?? ""}`} data-testid="import-error" className="text-destructive">
                  Line {e.line}
                  {e.field !== null && ` (${e.field})`}: {peopleAdminError(e.code, e.code)}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
      {done !== null && (
        <p data-testid="import-result" className="text-sm">
          Imported: {done.created} new · {done.updated} updated · {done.unchanged} unchanged
        </p>
      )}
      {error !== null && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
    </section>
  );
}
