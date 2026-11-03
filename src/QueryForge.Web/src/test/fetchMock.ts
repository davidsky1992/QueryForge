import { vi } from "vitest";

export type RecordedRequest = {
  method: string;
  path: string;
  headers: Record<string, string>;
  body: unknown;
};

type Route = {
  method: string;
  path: string | RegExp;
  status: number;
  body?: unknown;
  headers?: Record<string, string>;
};

/**
 * Replaces fetch with scripted responses. Routes are matched in order on method and path (without the API base URL);
 * a matched route is used once, so the same path can be scripted to answer differently on successive calls.
 */
export function mockFetch(routes: Route[]) {
  const pending = [...routes];
  const requests: RecordedRequest[] = [];

  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input));
    const path = url.pathname + url.search;
    const method = (init?.method ?? "GET").toUpperCase();
    requests.push({
      method,
      path,
      headers: { ...(init?.headers as Record<string, string> | undefined) },
      body: typeof init?.body === "string" ? JSON.parse(init.body) : undefined
    });

    const index = pending.findIndex(route => route.method === method &&
      (typeof route.path === "string" ? route.path === path : route.path.test(path)));
    if (index < 0) {
      throw new Error(`Unexpected request ${method} ${path}`);
    }

    const [route] = pending.splice(index, 1);
    const isJson = route.body !== undefined;
    return new Response(isJson ? JSON.stringify(route.body) : null, {
      status: route.status,
      headers: {
        ...(isJson ? { "content-type": route.status >= 400 ? "application/problem+json" : "application/json" } : {}),
        ...route.headers
      }
    });
  });

  vi.stubGlobal("fetch", fetchMock);
  return { requests };
}
