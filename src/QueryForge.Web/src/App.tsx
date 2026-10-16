import { FormEvent, useEffect, useMemo, useState } from "react";
import {
  createApiDefinition,
  deleteApiDefinition,
  listApiDefinitions,
  updateApiDefinition
} from "./api/apiDefinitions";
import { ApiError } from "./api/httpClient";
import {
  ApiAuthenticationType,
  ApiDefinition,
  ApiDefinitionFormValues,
  ApiDefinitionStatus,
  authenticationLabels,
  statusLabels
} from "./types/apiDefinition";

const emptyForm: ApiDefinitionFormValues = {
  name: "",
  slug: "",
  description: "",
  baseUrl: "",
  status: ApiDefinitionStatus.Draft,
  authenticationType: ApiAuthenticationType.None,
  documentationUrl: "",
  tags: ""
};

export default function App() {
  const [items, setItems] = useState<ApiDefinition[]>([]);
  const [selected, setSelected] = useState<ApiDefinition | null>(null);
  const [editing, setEditing] = useState<ApiDefinition | null>(null);
  const [form, setForm] = useState<ApiDefinitionFormValues>(emptyForm);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<ApiDefinitionStatus | "">("");
  const [pageNumber, setPageNumber] = useState(1);
  const [totalPages, setTotalPages] = useState(0);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const canGoBack = pageNumber > 1;
  const canGoForward = totalPages > 0 && pageNumber < totalPages;

  useEffect(() => {
    void load();
  }, [pageNumber, status]);

  const selectedDetails = useMemo(() => selected ?? items[0] ?? null, [items, selected]);

  async function load(nextSearch = search) {
    setIsLoading(true);
    setMessage(null);

    try {
      const result = await listApiDefinitions({
        pageNumber,
        pageSize: 10,
        search: nextSearch.trim(),
        status
      });
      setItems(result.items);
      setTotalPages(result.totalPages);
      setTotalCount(result.totalCount);
      setSelected(current => current && result.items.some(item => item.id === current.id) ? current : result.items[0] ?? null);
    } catch (error) {
      setMessage(toMessage(error));
    } finally {
      setIsLoading(false);
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setMessage(null);

    try {
      const saved = editing
        ? await updateApiDefinition(editing.id, editing.rowVersion, form)
        : await createApiDefinition(form);

      setMessage(editing ? "API definition updated." : "API definition created.");
      setEditing(null);
      setForm(emptyForm);
      setSelected(saved);
      await load();
    } catch (error) {
      await handleMutationError(error);
    }
  }

  async function handleDelete(apiDefinition: ApiDefinition) {
    if (!window.confirm(`Delete ${apiDefinition.name}?`)) {
      return;
    }

    try {
      await deleteApiDefinition(apiDefinition.id, apiDefinition.rowVersion);
      setMessage("API definition deleted.");
      setSelected(null);
      await load();
    } catch (error) {
      await handleMutationError(error);
    }
  }

  async function handleMutationError(error: unknown) {
    if (error instanceof ApiError && error.isConcurrencyConflict) {
      // The edit was based on an outdated copy; discard it and show the latest data.
      resetForm();
      await load();
      setMessage("This record was changed by someone else. The list has been reloaded; please review and try again.");
      return;
    }

    setMessage(toMessage(error));
  }

  function startEdit(apiDefinition: ApiDefinition) {
    setEditing(apiDefinition);
    setForm({
      name: apiDefinition.name,
      slug: apiDefinition.slug,
      description: apiDefinition.description ?? "",
      baseUrl: apiDefinition.baseUrl,
      status: apiDefinition.status,
      authenticationType: apiDefinition.authenticationType,
      documentationUrl: apiDefinition.documentationUrl ?? "",
      tags: apiDefinition.tags ?? ""
    });
  }

  function resetForm() {
    setEditing(null);
    setForm(emptyForm);
  }

  function updateForm<K extends keyof ApiDefinitionFormValues>(key: K, value: ApiDefinitionFormValues[K]) {
    setForm(current => ({ ...current, [key]: value }));
  }

  return (
    <main className="app-shell">
      <section className="workspace">
        <header className="topbar">
          <div>
            <p className="eyebrow">QueryForge</p>
            <h1>API Definitions</h1>
          </div>
          <div className="counter">
            <strong>{totalCount}</strong>
            <span>records</span>
          </div>
        </header>

        <section className="toolbar" aria-label="API definition filters">
          <input
            type="search"
            placeholder="Search name, slug, or tags"
            value={search}
            onChange={event => setSearch(event.target.value)}
            onKeyDown={event => {
              if (event.key === "Enter") {
                setPageNumber(1);
                void load(search);
              }
            }}
          />
          <select
            value={status}
            onChange={event => {
              setStatus(event.target.value === "" ? "" : Number(event.target.value) as ApiDefinitionStatus);
              setPageNumber(1);
            }}
          >
            <option value="">All statuses</option>
            {Object.entries(statusLabels).map(([value, label]) => (
              <option key={value} value={value}>{label}</option>
            ))}
          </select>
          <button type="button" onClick={() => { setPageNumber(1); void load(search); }}>
            Search
          </button>
        </section>

        {message && <div className="notice">{message}</div>}

        <section className="content-grid">
          <div className="table-panel">
            <table>
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Status</th>
                  <th>Auth</th>
                  <th>Base URL</th>
                  <th aria-label="Actions" />
                </tr>
              </thead>
              <tbody>
                {items.map(item => (
                  <tr key={item.id} className={selectedDetails?.id === item.id ? "selected-row" : ""}>
                    <td>
                      <button className="link-button" type="button" onClick={() => setSelected(item)}>
                        {item.name}
                      </button>
                      <span className="muted">{item.slug}</span>
                    </td>
                    <td><span className={`status status-${item.status}`}>{statusLabels[item.status]}</span></td>
                    <td>{authenticationLabels[item.authenticationType]}</td>
                    <td className="url-cell">{item.baseUrl}</td>
                    <td className="row-actions">
                      <button type="button" onClick={() => startEdit(item)}>Edit</button>
                      <button type="button" className="danger" onClick={() => void handleDelete(item)}>Delete</button>
                    </td>
                  </tr>
                ))}
                {!isLoading && items.length === 0 && (
                  <tr>
                    <td colSpan={5} className="empty">No API definitions found.</td>
                  </tr>
                )}
              </tbody>
            </table>

            <footer className="pagination">
              <button type="button" disabled={!canGoBack} onClick={() => setPageNumber(page => page - 1)}>
                Previous
              </button>
              <span>Page {pageNumber} of {totalPages || 1}</span>
              <button type="button" disabled={!canGoForward} onClick={() => setPageNumber(page => page + 1)}>
                Next
              </button>
            </footer>
          </div>

          <aside className="details-panel">
            <h2>{selectedDetails ? selectedDetails.name : "No selection"}</h2>
            {selectedDetails ? (
              <dl>
                <dt>Slug</dt>
                <dd>{selectedDetails.slug}</dd>
                <dt>Description</dt>
                <dd>{selectedDetails.description || "No description"}</dd>
                <dt>Documentation</dt>
                <dd>{selectedDetails.documentationUrl || "Not set"}</dd>
                <dt>Tags</dt>
                <dd>{selectedDetails.tags || "None"}</dd>
              </dl>
            ) : (
              <p className="muted">Create or select an API definition.</p>
            )}
          </aside>
        </section>
      </section>

      <aside className="form-panel">
        <div className="form-heading">
          <div>
            <p className="eyebrow">{editing ? "Edit" : "Create"}</p>
            <h2>{editing ? editing.name : "New API Definition"}</h2>
          </div>
          {editing && <button type="button" onClick={resetForm}>Cancel</button>}
        </div>

        <form onSubmit={event => void handleSubmit(event)}>
          <label>
            Name
            <input value={form.name} onChange={event => updateForm("name", event.target.value)} required maxLength={160} />
          </label>
          <label>
            Slug
            <input value={form.slug} onChange={event => updateForm("slug", event.target.value)} required maxLength={180} />
          </label>
          <label>
            Base URL
            <input value={form.baseUrl} onChange={event => updateForm("baseUrl", event.target.value)} required />
          </label>
          <label>
            Description
            <textarea value={form.description} onChange={event => updateForm("description", event.target.value)} rows={4} />
          </label>
          <div className="field-row">
            <label>
              Status
              <select value={form.status} onChange={event => updateForm("status", Number(event.target.value) as ApiDefinitionStatus)}>
                {Object.entries(statusLabels).map(([value, label]) => (
                  <option key={value} value={value}>{label}</option>
                ))}
              </select>
            </label>
            <label>
              Auth
              <select
                value={form.authenticationType}
                onChange={event => updateForm("authenticationType", Number(event.target.value) as ApiAuthenticationType)}
              >
                {Object.entries(authenticationLabels).map(([value, label]) => (
                  <option key={value} value={value}>{label}</option>
                ))}
              </select>
            </label>
          </div>
          <label>
            Documentation URL
            <input value={form.documentationUrl} onChange={event => updateForm("documentationUrl", event.target.value)} />
          </label>
          <label>
            Tags
            <input value={form.tags} onChange={event => updateForm("tags", event.target.value)} placeholder="orders,internal" />
          </label>
          <button type="submit" className="primary">
            {editing ? "Save changes" : "Create definition"}
          </button>
        </form>
      </aside>
    </main>
  );
}

function toMessage(error: unknown) {
  if (error instanceof ApiError) {
    return `${error.message} (${error.status})`;
  }

  return error instanceof Error ? error.message : "Unexpected error";
}
