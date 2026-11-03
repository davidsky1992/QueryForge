import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { mockFetch } from "../test/fetchMock";
import { AuditEvent, AuditOutcome } from "../types/execution";
import { AuditPanel } from "./AuditPanel";

function auditEvent(overrides: Partial<AuditEvent> = {}): AuditEvent {
  return {
    id: "a-1",
    occurredAtUtc: "2026-11-03T09:00:00Z",
    actor: "ops-admin",
    action: "operation.executed",
    targetType: "ApiOperation",
    targetId: "op-1",
    outcome: AuditOutcome.Succeeded,
    upstreamStatus: 200,
    durationMs: 143,
    errorCode: null,
    traceId: "trace-1",
    ...overrides
  };
}

const auditPath = /^\/api\/audit-events\?/;

describe("AuditPanel", () => {
  it("loads only when asked, then lists events", async () => {
    const { requests } = mockFetch([
      { method: "GET", path: auditPath, status: 200, body: { items: [auditEvent(), auditEvent({ id: "a-2", outcome: AuditOutcome.Rejected, errorCode: "validation_error", upstreamStatus: null, durationMs: null })], pageNumber: 1, pageSize: 20, totalCount: 2, totalPages: 1 } }
    ]);
    const user = userEvent.setup();
    render(<AuditPanel />);
    expect(requests).toHaveLength(0);

    await user.click(screen.getByRole("button", { name: "Load audit log" }));

    const table = await screen.findByRole("table");
    expect(within(table).getAllByText("operation.executed")).toHaveLength(2);
    expect(within(table).getByText("143 ms")).toBeInTheDocument();
    expect(within(table).getByText("validation_error")).toBeInTheDocument();
    expect(within(table).getByText("Rejected")).toBeInTheDocument();
  });

  it("sends the selected filters", async () => {
    const { requests } = mockFetch([{ method: "GET", path: auditPath, status: 200, body: { items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0 } }]);
    const user = userEvent.setup();
    render(<AuditPanel />);

    await user.selectOptions(screen.getByLabelText("Action"), "operation.updated");
    await user.selectOptions(screen.getByLabelText("Outcome"), "Failed");
    await user.type(screen.getByLabelText("From"), "2026-11-01T08:30");
    await user.click(screen.getByRole("button", { name: "Load audit log" }));

    await screen.findByText("No audit events match.");
    const query = new URLSearchParams(requests[0].path.split("?")[1]);
    expect(query.get("action")).toBe("operation.updated");
    expect(query.get("outcome")).toBe("1");
    expect(query.get("from")).toBe(new Date("2026-11-01T08:30").toISOString());
    expect(query.has("to")).toBe(false);
  });

  it("pages through results", async () => {
    const { requests } = mockFetch([
      { method: "GET", path: auditPath, status: 200, body: { items: [auditEvent()], pageNumber: 1, pageSize: 20, totalCount: 21, totalPages: 2 } },
      { method: "GET", path: auditPath, status: 200, body: { items: [auditEvent({ id: "a-21" })], pageNumber: 2, pageSize: 20, totalCount: 21, totalPages: 2 } }
    ]);
    const user = userEvent.setup();
    render(<AuditPanel />);

    await user.click(screen.getByRole("button", { name: "Load audit log" }));
    await user.click(await screen.findByRole("button", { name: "Next" }));

    expect(await screen.findByText("Page 2 of 2")).toBeInTheDocument();
    expect(new URLSearchParams(requests[1].path.split("?")[1]).get("pageNumber")).toBe("2");
    expect(screen.getByRole("button", { name: "Next" })).toBeDisabled();
  });

  it("explains a 403 for non-admin keys", async () => {
    mockFetch([{ method: "GET", path: auditPath, status: 403, body: { title: "Forbidden", status: 403 } }]);
    const user = userEvent.setup();
    render(<AuditPanel />);

    await user.click(screen.getByRole("button", { name: "Load audit log" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Reading the audit log requires an admin key.");
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});
