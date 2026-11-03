import { FormEvent, useState } from "react";
import { ApiError } from "../api/httpClient";
import { executeOperation, toParameterValues } from "../api/execution";
import { ExecutionResult } from "../types/execution";
import { ApiOperation, ParameterType } from "../types/operation";
import { toMessage, validationMessages } from "../utils/errors";

type Props = {
  definitionSlug: string;
  operation: ApiOperation;
  onClose: () => void;
};

/**
 * Runs one operation with caller-supplied parameter values and shows the response envelope. Values live only in
 * this component's state. The upstream body is untrusted and is only ever rendered as text.
 */
export function TryItPanel({ definitionSlug, operation, onClose }: Props) {
  const [inputs, setInputs] = useState<Record<string, string>>({});
  const [result, setResult] = useState<ExecutionResult | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<string[]>([]);
  const [running, setRunning] = useState(false);

  async function handleRun(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setRunning(true);
    setResult(null);
    setMessage(null);
    setFieldErrors([]);
    try {
      setResult(await executeOperation(definitionSlug, operation.key, toParameterValues(operation.parameters, inputs)));
    } catch (error) {
      setFieldErrors(validationMessages(error));
      setMessage(executionMessage(error));
    } finally {
      setRunning(false);
    }
  }

  return (
    <section className="try-it" aria-label={`Try ${operation.key}`}>
      <div className="form-heading">
        <h2>Try {operation.key}</h2>
        <button type="button" onClick={onClose}>Close</button>
      </div>

      <form aria-label={`Run ${operation.key}`} onSubmit={event => void handleRun(event)}>
        {operation.parameters.length === 0 && <p className="muted">This operation has no parameters.</p>}
        {operation.parameters.map(parameter => (
          <label key={parameter.name}>
            {parameter.name}{parameter.required ? " *" : ""}
            {parameter.type === ParameterType.Boolean ? (
              <select value={inputs[parameter.name] ?? ""} onChange={event => setInputs(current => ({ ...current, [parameter.name]: event.target.value }))}>
                <option value="">{parameter.default ? `(default: ${parameter.default})` : "(not set)"}</option>
                <option value="true">true</option>
                <option value="false">false</option>
              </select>
            ) : (
              <input
                value={inputs[parameter.name] ?? ""}
                placeholder={parameter.default ?? ""}
                autoComplete="off"
                onChange={event => setInputs(current => ({ ...current, [parameter.name]: event.target.value }))}
              />
            )}
          </label>
        ))}
        <button type="submit" className="primary" disabled={running}>{running ? "Running…" : "Run"}</button>
      </form>

      {message && <div className="notice" role="alert">{message}</div>}
      {fieldErrors.length > 0 && (
        <ul className="field-errors" aria-label="Parameter errors">
          {fieldErrors.map(error => <li key={error}>{error}</li>)}
        </ul>
      )}

      {result && (
        <div className="execution-result" aria-label="Execution result">
          <p>
            Upstream status <strong>{result.upstreamStatus}</strong>
            {" · "}{result.contentType ?? "no content type"}
            {" · "}{result.durationMs} ms
          </p>
          {/* Rendered as text: React escapes it, so markup in an upstream response is never interpreted. */}
          <pre>{formatBody(result.body)}</pre>
        </div>
      )}
    </section>
  );
}

function formatBody(body: unknown): string {
  if (body === null || body === undefined) {
    return "(empty body)";
  }

  return typeof body === "string" ? body : JSON.stringify(body, null, 2);
}

function executionMessage(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return toMessage(error);
  }

  const code = typeof error.details === "object" && error.details !== null && "code" in error.details
    ? String((error.details as { code: unknown }).code)
    : null;

  switch (error.status) {
    case 403:
      return "This API key cannot execute operations. Use an executor or admin key.";
    case 404:
      // The execute route does not exist while execution is disabled, which gives an empty 404.
      return code === null && error.details === ""
        ? "Execution is disabled on this server (Execution:Enabled is false)."
        : "The operation was not found.";
    case 400:
      return "Some parameter values are not valid.";
    default:
      return code ? `${error.message} (${error.status}, ${code})` : toMessage(error);
  }
}
