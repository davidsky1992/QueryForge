import { ApiError } from "../api/httpClient";

/** A short, user-facing message for an API error. Never includes API keys or request values. */
export function toMessage(error: unknown, writeAction = "Changing API definitions"): string {
  if (error instanceof ApiError) {
    switch (error.status) {
      case 401:
        return "The API key is missing or was not accepted. Enter a valid key.";
      case 403:
        return `This API key does not allow that action. ${writeAction} requires an admin key.`;
      case 429:
        return error.retryAfterSeconds
          ? `Too many requests. Try again in ${error.retryAfterSeconds} seconds.`
          : "Too many requests. Try again shortly.";
      default:
        return `${error.message} (${error.status})`;
    }
  }

  return error instanceof Error ? error.message : "Unexpected error";
}

/** Field messages from a ValidationProblemDetails response, as "field: message" lines. */
export function validationMessages(error: unknown): string[] {
  if (!(error instanceof ApiError) || error.status !== 400) {
    return [];
  }

  const details = error.details as { errors?: Record<string, string[]> } | null;
  return Object.entries(details?.errors ?? {}).flatMap(([field, messages]) => messages.map(message => `${field}: ${message}`));
}
