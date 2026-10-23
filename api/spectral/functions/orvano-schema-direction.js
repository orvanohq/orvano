// Spec 0014 JSW-11, CLT-2: request and response bodies are separate schemas, so no component schema is reachable from
// both a request body and a response. Reads the unresolved document, following $ref chains between components.
const methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];
const prefix = "#/components/schemas/";

function refs(node, found = new Set()) {
  if (Array.isArray(node)) {
    for (const item of node) refs(item, found);
  } else if (node !== null && typeof node === "object") {
    if (typeof node.$ref === "string" && node.$ref.startsWith(prefix))
      found.add(node.$ref.slice(prefix.length));
    for (const value of Object.values(node)) refs(value, found);
  }
  return found;
}

function reachable(roots, schemas) {
  const seen = new Set();
  const queue = [...roots];
  while (queue.length > 0) {
    const name = queue.pop();
    if (seen.has(name)) continue;
    seen.add(name);
    for (const next of refs(schemas[name])) queue.push(next);
  }
  return seen;
}

export default function orvanoSchemaDirection(document) {
  const schemas = document?.components?.schemas ?? {};
  const requests = new Set();
  const responses = new Set();
  for (const item of Object.values(document?.paths ?? {})) {
    for (const method of methods) {
      const operation = item?.[method];
      if (!operation) continue;
      refs(operation.requestBody, requests);
      refs(operation.responses, responses);
    }
  }
  const inResponses = reachable(responses, schemas);
  return [...reachable(requests, schemas)]
    .filter((name) => inResponses.has(name))
    .toSorted()
    .map((name) => ({
      message: `Schema ${name} is used by both a request body and a response; give each direction its own record.`,
      path: ["components", "schemas", name],
    }));
}
