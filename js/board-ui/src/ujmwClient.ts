// Minimal UJMW client: POST <serviceUrl><MethodName> with the arguments as JSON object,
// the response is a wrapper with 'return' (and optionally 'fault').
// All URLs are relative, so the UI works under any mount path.

export class UjmwFaultError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "UjmwFaultError";
  }
}

function toCamelCase(name: string): string {
  if (name.length === 0) {
    return name;
  }
  return name.charAt(0).toLowerCase() + name.substring(1);
}

/**
 * Normalizes all object keys to camelCase (the host serializer may use PascalCase or camelCase).
 */
export function normalizeKeys(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map((item) => normalizeKeys(item));
  }
  if (value !== null && typeof value === "object") {
    const result: Record<string, unknown> = {};
    for (const [key, inner] of Object.entries(value as Record<string, unknown>)) {
      result[toCamelCase(key)] = normalizeKeys(inner);
    }
    return result;
  }
  return value;
}

export async function invokeUjmw<TReturn>(
  serviceUrl: string,
  methodName: string,
  requestDto: Record<string, unknown>
): Promise<TReturn> {
  const response = await fetch(serviceUrl + methodName, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(requestDto)
  });

  const text = await response.text();
  let json: Record<string, unknown> | null = null;

  if (text.length > 0) {
    try {
      json = normalizeKeys(JSON.parse(text)) as Record<string, unknown>;
    } catch {
      throw new Error(`HTTP ${response.status}: ${text.substring(0, 500)}`);
    }
  }

  const fault = json !== null ? json["fault"] : null;

  if (typeof fault === "string" && fault.length > 0) {
    throw new UjmwFaultError(fault);
  }

  if (!response.ok) {
    throw new Error(`HTTP ${response.status}: ${text.substring(0, 500)}`);
  }

  if (json === null) {
    return null as TReturn;
  }

  return json["return"] as TReturn;
}
