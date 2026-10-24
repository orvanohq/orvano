// Spec 0014 PAG-1, PAG-2, PAG-7, CLT-2: every list is paged. A 200 response holding an array of items needs
// x-orvano-paging, and a paged operation takes `limit` and `cursor` and returns { items, nextCursor, total }.
const types = (schema) => [schema?.type ?? []].flat();

export default function orvanoPaging(operation, _options, context) {
  if (operation === null || typeof operation !== "object") return [];
  const schema = operation.responses?.["200"]?.content?.["application/json"]?.schema;
  const paging = operation["x-orvano-paging"];
  const path = [...context.path];
  const pageShaped = types(schema?.properties?.items).includes("array");

  if (paging === undefined) {
    return types(schema).includes("array") || pageShaped
      ? [{ message: "A list response needs x-orvano-paging, so it is paged (PAG-7).", path }]
      : [];
  }

  const results = [];
  if (typeof paging?.total !== "boolean") {
    results.push({
      message: "x-orvano-paging is { total: boolean } (XTN-1).",
      path: [...path, "x-orvano-paging"],
    });
  }
  const query = new Set(
    (operation.parameters ?? [])
      .filter((parameter) => parameter?.in === "query")
      .map((parameter) => parameter.name),
  );
  for (const name of ["limit", "cursor"]) {
    if (!query.has(name))
      results.push({
        message: `A paged operation takes the query parameter ${name} (PAG-1).`,
        path,
      });
  }
  const properties = schema?.properties ?? {};
  if (!pageShaped || !("nextCursor" in properties) || !("total" in properties)) {
    results.push({
      message: "A paged operation returns { items, nextCursor, total } (PAG-2).",
      path,
    });
  }
  return results;
}
