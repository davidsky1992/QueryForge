import { describe, expect, it } from "vitest";
import { mockFetch } from "../test/fetchMock";
import { AuditOutcome } from "../types/execution";
import { OperationParameter, ParameterLocation, ParameterType } from "../types/operation";
import { executeOperation, listAuditEvents, toParameterValues } from "./execution";

function parameter(name: string, type: ParameterType): OperationParameter {
  return { name, location: ParameterLocation.Query, type, required: false, maxLength: null, minimum: null, maximum: null, pattern: null, allowedValues: [], default: null };
}

describe("execution api", () => {
  it("converts inputs by declared type and leaves empty inputs out", () => {
    const parameters = [
      parameter("city", ParameterType.String),
      parameter("days", ParameterType.Integer),
      parameter("ratio", ParameterType.Number),
      parameter("metric", ParameterType.Boolean),
      parameter("unset", ParameterType.String)
    ];

    expect(toParameterValues(parameters, { city: "Hanoi", days: "2", ratio: "2.5", metric: "false", unset: "" }))
      .toEqual({ city: "Hanoi", days: 2, ratio: 2.5, metric: false });
  });

  it("sends unparseable numbers as text so the API reports them", () => {
    expect(toParameterValues([parameter("days", ParameterType.Integer)], { days: "two" })).toEqual({ days: "two" });
  });

  it("ignores inputs for undeclared parameters", () => {
    expect(toParameterValues([parameter("city", ParameterType.String)], { city: "Hanoi", admin: "true" })).toEqual({ city: "Hanoi" });
  });

  it("posts only the parameters to the execute route", async () => {
    const { requests } = mockFetch([{ method: "POST", path: "/api/execute/weather/current", status: 200, body: {} }]);

    await executeOperation("weather", "current", { city: "Hanoi" });

    expect(requests[0].body).toEqual({ parameters: { city: "Hanoi" } });
  });

  it("builds the audit query from the filters", async () => {
    const { requests } = mockFetch([{ method: "GET", path: /^\/api\/audit-events\?/, status: 200, body: { items: [], pageNumber: 2, pageSize: 20, totalCount: 0, totalPages: 0 } }]);

    await listAuditEvents({ pageNumber: 2, pageSize: 20, action: "operation.executed", outcome: AuditOutcome.Failed, from: "2026-11-01T00:00:00.000Z" });

    const query = new URLSearchParams(requests[0].path.split("?")[1]);
    expect(Object.fromEntries(query)).toEqual({ pageNumber: "2", pageSize: "20", action: "operation.executed", outcome: "1", from: "2026-11-01T00:00:00.000Z" });
  });
});
