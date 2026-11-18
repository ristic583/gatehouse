import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MAX_PHOTO_BYTES, PhotoCard } from "./photo-card";

const fetchMock = vi.fn<typeof fetch>();

function renderCard() {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <PhotoCard personId="p-1" />
    </QueryClientProvider>,
  );
}

function file(size: number, type: string): File {
  return new File([new Uint8Array(size)], "photo", { type });
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("PhotoCard", () => {
  it("blocks an oversize file before uploading", () => {
    renderCard();

    fireEvent.change(screen.getByLabelText("Photo file"), { target: { files: [file(MAX_PHOTO_BYTES + 1, "image/jpeg")] } });

    expect(screen.getByRole("alert")).toHaveTextContent("larger than 2 MiB");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("shows the server error", async () => {
    fetchMock.mockImplementation(() =>
      Promise.resolve(
        new Response(JSON.stringify({ code: "photo.unsupported_type", detail: "Only JPEG and PNG photos are accepted.", status: 400 }), {
          status: 400,
        }),
      ),
    );
    renderCard();

    fireEvent.change(screen.getByLabelText("Photo file"), { target: { files: [file(100, "image/png")] } });

    expect(await screen.findByRole("alert")).toHaveTextContent("Only JPEG and PNG photos are accepted.");
    expect(fetchMock.mock.calls[0]![0]).toBe("/api/proxy/v1/me/photo");
    expect(fetchMock.mock.calls[0]![1]!.method).toBe("PUT");
  });
});
