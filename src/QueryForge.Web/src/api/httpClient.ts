// Unset: local `npm run dev` against the API on its launchSettings port.
// Empty string (Docker image default): same-origin relative URLs, proxied to the API by nginx.
const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5111";

// The API key is entered by the user and kept in memory only: never in the bundle, storage, cookies, or URLs.
// It is lost on page reload by design.
let apiKey: string | null = null;

export function setApiKey(key: string | null) {
  apiKey = key?.trim() || null;
}

export function hasApiKey() {
  return apiKey !== null;
}

export class ApiError extends Error {
  public readonly status: number;
  public readonly details: unknown;
  public readonly retryAfterSeconds: number | null;

  public constructor(message: string, status: number, details: unknown, retryAfterSeconds: number | null = null) {
    super(message);
    this.status = status;
    this.details = details;
    this.retryAfterSeconds = retryAfterSeconds;
  }

  public get isConcurrencyConflict(): boolean {
    return this.status === 409
      && typeof this.details === "object"
      && this.details !== null
      && "code" in this.details
      && this.details.code === "concurrency_conflict";
  }
}

export async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(apiKey ? { "X-Api-Key": apiKey } : {}),
      ...options.headers
    }
  });

  if (response.status === 204) {
    return undefined as T;
  }

  // Matches application/json and application/problem+json (all API error responses).
  const contentType = response.headers.get("content-type") ?? "";
  const body = contentType.includes("json") ? await response.json() : await response.text();

  if (!response.ok) {
    const message = typeof body === "object" && body !== null && "title" in body
      ? String(body.title)
      : "Request failed";
    const retryAfter = Number(response.headers.get("retry-after"));
    throw new ApiError(message, response.status, body, Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : null);
  }

  return body as T;
}
