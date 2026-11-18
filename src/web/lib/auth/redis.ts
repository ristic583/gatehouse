import { Redis } from "ioredis";

export interface RedisOptions {
  host: string;
  port: number;
  password?: string;
  tls: boolean;
}

/** Parses the StackExchange.Redis format used by Aspire: `host:port,password=xxx[,ssl=true]`. */
export function parseRedisConnectionString(connectionString: string): RedisOptions {
  const [endpoint = "", ...options] = connectionString.split(",").map((part) => part.trim());
  const separator = endpoint.lastIndexOf(":");
  const host = separator > 0 ? endpoint.slice(0, separator) : endpoint;
  const port = separator > 0 ? Number.parseInt(endpoint.slice(separator + 1), 10) : 6379;

  const result: RedisOptions = { host: host || "localhost", port: Number.isNaN(port) ? 6379 : port, tls: false };
  for (const option of options) {
    const eq = option.indexOf("=");
    if (eq < 0) {
      continue;
    }
    const key = option.slice(0, eq).trim().toLowerCase();
    const value = option.slice(eq + 1).trim();
    if (key === "password") {
      result.password = value;
    } else if (key === "ssl") {
      result.tls = value.toLowerCase() === "true";
    }
  }
  return result;
}

let client: Redis | undefined;

export async function getRedis(): Promise<Redis> {
  if (client === undefined) {
    const { getAuthConfig } = await import("./config");
    const options = parseRedisConnectionString(getAuthConfig().redisConnectionString);
    client = new Redis({
      host: options.host,
      port: options.port,
      password: options.password,
      tls: options.tls ? {} : undefined,
      lazyConnect: false,
      maxRetriesPerRequest: 3,
    });
  }
  return client;
}
