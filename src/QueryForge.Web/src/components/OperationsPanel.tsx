import { FormEvent, useEffect, useState } from "react";
import { ApiError } from "../api/httpClient";
import { createOperation, deleteOperation, listOperations, updateOperation } from "../api/operations";
import { ApiDefinition } from "../types/apiDefinition";
import {
  ApiOperation,
  locationLabels,
  OperationFormValues,
  ParameterFormValues,
  ParameterLocation,
  ParameterType,
  parameterTypeLabels
} from "../types/operation";
import { toMessage, validationMessages } from "../utils/errors";

const emptyParameter: ParameterFormValues = {
  name: "",
  location: ParameterLocation.Query,
  type: ParameterType.String,
  required: false,
  maxLength: "",
  minimum: "",
  maximum: "",
  pattern: "",
  allowedValues: "",
  default: ""
};

const emptyOperation: OperationFormValues = {
  key: "",
  pathTemplate: "/",
  timeoutSeconds: "10",
  maxResponseBytes: "262144",
  enabled: true,
  parameters: []
};

type Props = {
  definition: ApiDefinition;
};

/** Lists, creates, edits and deletes the executable operations of one API definition. */
export function OperationsPanel({ definition }: Props) {
  const [operations, setOperations] = useState<ApiOperation[]>([]);
  const [editing, setEditing] = useState<ApiOperation | null>(null);
  const [form, setForm] = useState<OperationFormValues | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<string[]>([]);

  useEffect(() => {
    setForm(null);
    setEditing(null);
    setMessage(null);
    void load();
  }, [definition.id]);

  async function load() {
    try {
      setOperations(await listOperations(definition.id));
    } catch (error) {
      setOperations([]);
      setMessage(toMessage(error, "Changing operations"));
    }
  }

  function startCreate() {
    setEditing(null);
    setForm({ ...emptyOperation, parameters: [] });
    setFieldErrors([]);
    setMessage(null);
  }

  function startEdit(operation: ApiOperation) {
    setEditing(operation);
    setFieldErrors([]);
    setMessage(null);
    setForm({
      key: operation.key,
      pathTemplate: operation.pathTemplate,
      timeoutSeconds: String(operation.timeoutSeconds),
      maxResponseBytes: String(operation.maxResponseBytes),
      enabled: operation.enabled,
      parameters: operation.parameters.map(parameter => ({
        name: parameter.name,
        location: parameter.location,
        type: parameter.type,
        required: parameter.required,
        maxLength: parameter.maxLength?.toString() ?? "",
        minimum: parameter.minimum?.toString() ?? "",
        maximum: parameter.maximum?.toString() ?? "",
        pattern: parameter.pattern ?? "",
        allowedValues: parameter.allowedValues.join(", "),
        default: parameter.default ?? ""
      }))
    });
  }

  function cancel() {
    setForm(null);
    setEditing(null);
    setFieldErrors([]);
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!form) {
      return;
    }

    setMessage(null);
    setFieldErrors([]);
    try {
      if (editing) {
        await updateOperation(definition.id, editing.id, editing.rowVersion, form);
        setMessage("Operation updated.");
      } else {
        await createOperation(definition.id, form);
        setMessage("Operation created.");
      }

      cancel();
      await load();
    } catch (error) {
      await handleMutationError(error);
    }
  }

  async function handleDelete(operation: ApiOperation) {
    if (!window.confirm(`Delete operation ${operation.key}?`)) {
      return;
    }

    try {
      await deleteOperation(definition.id, operation.id, operation.rowVersion);
      setMessage("Operation deleted.");
      if (editing?.id === operation.id) {
        cancel();
      }

      await load();
    } catch (error) {
      await handleMutationError(error);
    }
  }

  async function handleMutationError(error: unknown) {
    if (error instanceof ApiError && error.isConcurrencyConflict) {
      // The edit was based on an outdated copy; discard it and show the latest data.
      cancel();
      await load();
      setMessage("This operation was changed by someone else. The list has been reloaded; please review and try again.");
      return;
    }

    setFieldErrors(validationMessages(error));
    setMessage(toMessage(error, "Changing operations"));
  }

  function updateForm<K extends keyof OperationFormValues>(key: K, value: OperationFormValues[K]) {
    setForm(current => (current ? { ...current, [key]: value } : current));
  }

  function updateParameter<K extends keyof ParameterFormValues>(index: number, key: K, value: ParameterFormValues[K]) {
    setForm(current => current && {
      ...current,
      parameters: current.parameters.map((parameter, position) => (position === index ? { ...parameter, [key]: value } : parameter))
    });
  }

  function addParameter() {
    setForm(current => current && { ...current, parameters: [...current.parameters, { ...emptyParameter }] });
  }

  function removeParameter(index: number) {
    setForm(current => current && { ...current, parameters: current.parameters.filter((_, position) => position !== index) });
  }

  return (
    <section className="operations-panel" aria-label="Operations">
      <div className="form-heading">
        <h2>Operations</h2>
        <button type="button" onClick={startCreate}>New operation</button>
      </div>

      {message && <div className="notice" role="status">{message}</div>}
      {fieldErrors.length > 0 && (
        <ul className="field-errors" aria-label="Validation errors">
          {fieldErrors.map(error => <li key={error}>{error}</li>)}
        </ul>
      )}

      <table>
        <thead>
          <tr>
            <th>Key</th>
            <th>Path</th>
            <th>Parameters</th>
            <th>Enabled</th>
            <th aria-label="Actions" />
          </tr>
        </thead>
        <tbody>
          {operations.map(operation => (
            <tr key={operation.id}>
              <td>{operation.key}</td>
              <td className="url-cell">GET {operation.pathTemplate}</td>
              <td>{operation.parameters.map(parameter => parameter.name).join(", ") || "None"}</td>
              <td>{operation.enabled ? "Yes" : "No"}</td>
              <td className="row-actions">
                <button type="button" onClick={() => startEdit(operation)}>Edit</button>
                <button type="button" className="danger" onClick={() => void handleDelete(operation)}>Delete</button>
              </td>
            </tr>
          ))}
          {operations.length === 0 && (
            <tr>
              <td colSpan={5} className="empty">No operations yet.</td>
            </tr>
          )}
        </tbody>
      </table>

      {form && (
        <form className="operation-form" aria-label={editing ? `Edit operation ${editing.key}` : "New operation"} onSubmit={event => void handleSubmit(event)}>
          <div className="field-row">
            <label>
              Key
              <input value={form.key} onChange={event => updateForm("key", event.target.value)} required maxLength={64} />
            </label>
            <label>
              Path template
              <input value={form.pathTemplate} onChange={event => updateForm("pathTemplate", event.target.value)} required maxLength={512} placeholder="/v1/forecast/{city}" />
            </label>
          </div>
          <div className="field-row">
            <label>
              Timeout (seconds)
              <input value={form.timeoutSeconds} onChange={event => updateForm("timeoutSeconds", event.target.value)} inputMode="numeric" />
            </label>
            <label>
              Max response bytes
              <input value={form.maxResponseBytes} onChange={event => updateForm("maxResponseBytes", event.target.value)} inputMode="numeric" />
            </label>
            <label className="checkbox">
              <input type="checkbox" checked={form.enabled} onChange={event => updateForm("enabled", event.target.checked)} />
              Enabled
            </label>
          </div>

          <fieldset>
            <legend>Parameters</legend>
            {form.parameters.map((parameter, index) => {
              const label = `Parameter ${index + 1}`;
              return (
                <div className="parameter-row" key={index} role="group" aria-label={label}>
                  <input aria-label={`${label} name`} placeholder="name" value={parameter.name} onChange={event => updateParameter(index, "name", event.target.value)} />
                  <select aria-label={`${label} location`} value={parameter.location} onChange={event => updateParameter(index, "location", Number(event.target.value) as ParameterLocation)}>
                    {Object.entries(locationLabels).map(([value, text]) => <option key={value} value={value}>{text}</option>)}
                  </select>
                  <select aria-label={`${label} type`} value={parameter.type} onChange={event => updateParameter(index, "type", Number(event.target.value) as ParameterType)}>
                    {Object.entries(parameterTypeLabels).map(([value, text]) => <option key={value} value={value}>{text}</option>)}
                  </select>
                  <label className="checkbox">
                    <input type="checkbox" aria-label={`${label} required`} checked={parameter.required} onChange={event => updateParameter(index, "required", event.target.checked)} />
                    Required
                  </label>
                  <input aria-label={`${label} max length`} placeholder="max length" value={parameter.maxLength} onChange={event => updateParameter(index, "maxLength", event.target.value)} />
                  <input aria-label={`${label} minimum`} placeholder="min" value={parameter.minimum} onChange={event => updateParameter(index, "minimum", event.target.value)} />
                  <input aria-label={`${label} maximum`} placeholder="max" value={parameter.maximum} onChange={event => updateParameter(index, "maximum", event.target.value)} />
                  <input aria-label={`${label} pattern`} placeholder="pattern" value={parameter.pattern} onChange={event => updateParameter(index, "pattern", event.target.value)} />
                  <input aria-label={`${label} allowed values`} placeholder="allowed, values" value={parameter.allowedValues} onChange={event => updateParameter(index, "allowedValues", event.target.value)} />
                  <input aria-label={`${label} default`} placeholder="default" value={parameter.default} onChange={event => updateParameter(index, "default", event.target.value)} />
                  <button type="button" className="danger" onClick={() => removeParameter(index)}>Remove</button>
                </div>
              );
            })}
            <button type="button" onClick={addParameter}>Add parameter</button>
          </fieldset>

          <div className="row-actions">
            <button type="button" onClick={cancel}>Cancel</button>
            <button type="submit" className="primary">{editing ? "Save operation" : "Create operation"}</button>
          </div>
        </form>
      )}
    </section>
  );
}
