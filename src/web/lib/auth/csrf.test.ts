import { describe, expect, it } from "vitest";
import { isSameOrigin } from "./csrf";

const publicUrl = "http://localhost:3000";

function post(origin?: string): Request {
  const headers = new Headers();
  if (origin !== undefined) {
    headers.set("origin", origin);
  }
  return new Request("http://localhost:3000/api/proxy/v1/things", { method: "POST", headers });
}

describe("isSameOrigin", () => {
  it("accepts same origin", () => {
    expect(isSameOrigin(post("http://localhost:3000"), publicUrl)).toBe(true);
  });

  it("rejects foreign origin", () => {
    expect(isSameOrigin(post("https://evil.example"), publicUrl)).toBe(false);
  });

  it("rejects missing origin on POST", () => {
    expect(isSameOrigin(post(), publicUrl)).toBe(false);
  });
});
