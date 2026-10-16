// Unset: local `npm run dev` against the API on its launchSettings port.
// Empty string (Docker image default): same-origin relative URLs, proxied to the API by nginx.
const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5111";

export class ApiError extends Error {
  public readonly status: number;
  public readonly details: unknown;

  public constructor(message: string, status: number, details: unknown) {
    super(message);
    this.status = status;
    this.details = details;
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
      ...options.headers
    }
  });

  if (response.status === 204) {
    return undefined as T;
  }

  const contentType = response.headers.get("content-type") ?? "";
  const body = contentType.includes("application/json") ? await response.json() : await response.text();

  if (!response.ok) {
    const message = typeof body === "object" && body !== null && "title" in body
      ? String(body.title)
      : "Request failed";
    throw new ApiError(message, response.status, body);
  }

  return body as T;
}
