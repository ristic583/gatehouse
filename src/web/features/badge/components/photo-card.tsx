"use client";

import { useMutation } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ApiError, apiClient } from "@/lib/api/client";

export const MAX_PHOTO_BYTES = 2 * 1024 * 1024;
const ACCEPTED = ["image/jpeg", "image/png"];

const messageOf = (error: Error | null) => (error instanceof ApiError ? error.detail : error?.message);

/** My photo: JPEG/PNG up to 2 MiB; the server re-encodes it and strips metadata. */
export function PhotoCard({ personId }: { personId: string }) {
  const input = useRef<HTMLInputElement>(null);
  const [version, setVersion] = useState(0);
  const [hasPhoto, setHasPhoto] = useState(true);
  const [clientError, setClientError] = useState<string | null>(null);
  const upload = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData();
      form.append("file", file);
      return apiClient.upload<undefined>("PUT", "/v1/me/photo", form);
    },
    onSuccess: () => {
      setHasPhoto(true);
      setVersion((v) => v + 1);
    },
  });
  const remove = useMutation({
    mutationFn: () => apiClient.delete<undefined>("/v1/me/photo"),
    onSuccess: () => setHasPhoto(false),
  });
  const error = clientError ?? messageOf(upload.error ?? remove.error);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Photo</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3 text-sm">
        {hasPhoto ? (
          // eslint-disable-next-line @next/next/no-img-element -- private, uncached image through the BFF
          <img
            key={version}
            data-testid="my-photo"
            src={`/api/proxy/v1/people/${personId}/photo?v=${version}`}
            alt="Your photo"
            className="h-32 w-32 rounded-md object-cover"
            onError={() => setHasPhoto(false)}
          />
        ) : (
          <p>No photo yet.</p>
        )}
        <input
          ref={input}
          type="file"
          aria-label="Photo file"
          accept={ACCEPTED.join(",")}
          onChange={(e) => {
            const file = e.target.files?.[0];
            setClientError(null);
            if (file === undefined) {
              return;
            }
            if (file.size > MAX_PHOTO_BYTES) {
              setClientError("The photo is larger than 2 MiB.");
              return;
            }
            if (!ACCEPTED.includes(file.type)) {
              setClientError("Only JPEG and PNG photos are accepted.");
              return;
            }
            upload.mutate(file);
          }}
        />
        {hasPhoto ? (
          <Button variant="outline" className="self-start" disabled={remove.isPending} onClick={() => remove.mutate()}>
            Remove photo
          </Button>
        ) : null}
        {upload.isSuccess ? <p role="status">Photo saved.</p> : null}
        {error !== undefined && error !== null ? <p role="alert">{error}</p> : null}
      </CardContent>
    </Card>
  );
}
