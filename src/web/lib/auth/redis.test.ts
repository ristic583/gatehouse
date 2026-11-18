import { describe, expect, it } from "vitest";
import { parseRedisConnectionString } from "./redis";

describe("parseRedisConnectionString", () => {
  it("parses host, port and password", () => {
    expect(parseRedisConnectionString("localhost:6380,password=s3cret")).toEqual({
      host: "localhost",
      port: 6380,
      password: "s3cret",
      tls: false,
    });
  });

  it("parses ssl flag", () => {
    const options = parseRedisConnectionString("cache.example.com:6380,password=x,ssl=true");
    expect(options.tls).toBe(true);
    expect(options.host).toBe("cache.example.com");
  });
});
