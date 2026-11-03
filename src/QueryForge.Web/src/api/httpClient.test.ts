import { describe, expect, it } from "vitest";
import { mockFetch } from "../test/fetchMock";
import { ApiError, request, setApiKey } from "./httpClient";

async function failure(promise: Promise<unknown>): Promise<ApiError> {
  try {
    await promise;
  } catch (error) {
    return error as ApiError;
  }

  throw new Error("Expected the request to fail.");
}

describe("httpClient", () => {
  it("sends the in-memory API key in the X-Api-Key header, never in the URL", async () => {
    const { requests } = mockFetch([{ method: "GET", path: "/api/things", status: 200, body: [] }]);
    setApiKey("  secret-key  ");

    await request("/api/things");

    expect(requests[0].headers["X-Api-Key"]).toBe("secret-key");
    expect(requests[0].path).not.toContain("secret-key");
  });

  it("sends no key header when no key is set", async () => {
    const { requests } = mockFetch([{ method: "GET", path: "/api/things", status: 200, body: [] }]);

    await request("/api/things");

    expect(requests[0].headers).not.toHaveProperty("X-Api-Key");
  });

  it("reads problem+json errors, the retry delay and concurrency conflicts", async () => {
    mockFetch([
      { method: "GET", path: "/api/limited", status: 429, body: { title: "Too many requests", status: 429 }, headers: { "retry-after": "30" } },
      { method: "PUT", path: "/api/conflict", status: 409, body: { title: "Concurrency conflict", status: 409, code: "concurrency_conflict" } }
    ]);

    const limited = await failure(request("/api/limited"));
    const conflict = await failure(request("/api/conflict", { method: "PUT" }));

    expect(limited).toBeInstanceOf(ApiError);
    expect(limited.status).toBe(429);
    expect(limited.retryAfterSeconds).toBe(30);
    expect(conflict.isConcurrencyConflict).toBe(true);
  });
});
