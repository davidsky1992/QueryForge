import { useState } from "react";
import { ApiError } from "../api/httpClient";
import { AuditQuery, listAuditEvents } from "../api/execution";
import { AuditEvent, AuditOutcome, auditActions, auditOutcomeLabels } from "../types/execution";
import { toMessage } from "../utils/errors";

const pageSize = 20;

type Filters = {
  action: string;
  outcome: AuditOutcome | "";
  from: string;
  to: string;
};

/** Read-only audit log for admin keys. Loads on request, so reader keys are not met with an immediate 403. */
export function AuditPanel() {
  const [filters, setFilters] = useState<Filters>({ action: "", outcome: "", from: "", to: "" });
  const [events, setEvents] = useState<AuditEvent[] | null>(null);
  const [pageNumber, setPageNumber] = useState(1);
  const [totalPages, setTotalPages] = useState(0);
  const [message, setMessage] = useState<string | null>(null);

  async function load(page = 1) {
    setMessage(null);
    const query: AuditQuery = {
      pageNumber: page,
      pageSize,
      action: filters.action || undefined,
      outcome: filters.outcome === "" ? undefined : filters.outcome,
      // datetime-local values are in the browser's time zone; the API takes absolute timestamps.
      from: filters.from ? new Date(filters.from).toISOString() : undefined,
      to: filters.to ? new Date(filters.to).toISOString() : undefined
    };

    try {
      const result = await listAuditEvents(query);
      setEvents(result.items);
      setPageNumber(result.pageNumber);
      setTotalPages(result.totalPages);
    } catch (error) {
      setEvents(null);
      setMessage(error instanceof ApiError && error.status === 403
        ? "Reading the audit log requires an admin key."
        : toMessage(error));
    }
  }

  return (
    <section className="audit-panel" aria-label="Audit log">
      <div className="form-heading">
        <h2>Audit log</h2>
      </div>

      <form className="toolbar audit-filters" aria-label="Audit filters" onSubmit={event => { event.preventDefault(); void load(1); }}>
        <label>
          Action
          <select value={filters.action} onChange={event => setFilters(current => ({ ...current, action: event.target.value }))}>
            <option value="">All actions</option>
            {auditActions.map(action => <option key={action} value={action}>{action}</option>)}
          </select>
        </label>
        <label>
          Outcome
          <select
            value={filters.outcome}
            onChange={event => setFilters(current => ({ ...current, outcome: event.target.value === "" ? "" : Number(event.target.value) as AuditOutcome }))}
          >
            <option value="">All outcomes</option>
            {Object.entries(auditOutcomeLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
        </label>
        <label>
          From
          <input type="datetime-local" value={filters.from} onChange={event => setFilters(current => ({ ...current, from: event.target.value }))} />
        </label>
        <label>
          To
          <input type="datetime-local" value={filters.to} onChange={event => setFilters(current => ({ ...current, to: event.target.value }))} />
        </label>
        <button type="submit">Load audit log</button>
      </form>

      {message && <div className="notice" role="alert">{message}</div>}

      {events && (
        <>
          <table>
            <thead>
              <tr>
                <th>Time</th>
                <th>Actor</th>
                <th>Action</th>
                <th>Target</th>
                <th>Outcome</th>
                <th>Upstream</th>
                <th>Duration</th>
                <th>Error</th>
              </tr>
            </thead>
            <tbody>
              {events.map(item => (
                <tr key={item.id}>
                  <td>{new Date(item.occurredAtUtc).toLocaleString()}</td>
                  <td>{item.actor}</td>
                  <td>{item.action}</td>
                  <td className="url-cell">{item.targetId ?? "—"}</td>
                  <td>{auditOutcomeLabels[item.outcome]}</td>
                  <td>{item.upstreamStatus ?? "—"}</td>
                  <td>{item.durationMs === null ? "—" : `${item.durationMs} ms`}</td>
                  <td>{item.errorCode ?? "—"}</td>
                </tr>
              ))}
              {events.length === 0 && (
                <tr>
                  <td colSpan={8} className="empty">No audit events match.</td>
                </tr>
              )}
            </tbody>
          </table>
          <footer className="pagination">
            <button type="button" disabled={pageNumber <= 1} onClick={() => void load(pageNumber - 1)}>Previous</button>
            <span>Page {pageNumber} of {totalPages || 1}</span>
            <button type="button" disabled={totalPages === 0 || pageNumber >= totalPages} onClick={() => void load(pageNumber + 1)}>Next</button>
          </footer>
        </>
      )}
    </section>
  );
}
