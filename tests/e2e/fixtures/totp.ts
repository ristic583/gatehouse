import { createHmac } from "node:crypto";

/**
 * RFC 6238 TOTP (HMAC-SHA1, 6 digits, 30 s) with Node `crypto` only.
 * Keycloak stores the OTP secret as a raw string; its HMAC key is the UTF-8 bytes of that string.
 */
export function totp(rawSecret: string, nowMs = Date.now(), stepSeconds = 30, digits = 6): string {
  const counter = Math.floor(nowMs / 1000 / stepSeconds);
  const message = Buffer.alloc(8);
  message.writeBigUInt64BE(BigInt(counter));
  const hmac = createHmac("sha1", Buffer.from(rawSecret, "utf8")).update(message).digest();
  const offset = hmac[hmac.length - 1]! & 0x0f;
  const binary = ((hmac[offset]! & 0x7f) << 24) | (hmac[offset + 1]! << 16) | (hmac[offset + 2]! << 8) | hmac[offset + 3]!;
  return String(binary % 10 ** digits).padStart(digits, "0");
}

let lastCounter = -1;

/**
 * A code for a 30 s step this process has not used yet (Keycloak refuses a replayed code), with at least `minSeconds` left
 * so a slow form submit does not cross a step boundary.
 */
export async function freshTotp(rawSecret: string, minSeconds = 5): Promise<string> {
  for (;;) {
    const nowSeconds = Date.now() / 1000;
    const counter = Math.floor(nowSeconds / 30);
    const left = 30 - (nowSeconds % 30);
    if (counter > lastCounter && left >= minSeconds) {
      lastCounter = counter;
      return totp(rawSecret);
    }
    await new Promise((resolve) => setTimeout(resolve, (left + 0.5) * 1000));
  }
}
