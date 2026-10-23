// Spec 0014 PRB-1, CLT-2: every error response (4xx, 5xx, default) is application/problem+json with the shared
// Problem schema. Reads the unresolved document, so the $ref itself is checked.
export default function orvanoProblemResponse(response, _options, context) {
  const status = String(context.path[context.path.length - 1]);
  if (status !== "default" && !/^[45]/.test(status)) return [];
  const schema = response?.content?.["application/problem+json"]?.schema;
  return schema?.$ref === "#/components/schemas/Problem"
    ? []
    : [
        {
          message: `The ${status} response must be application/problem+json with the Problem schema.`,
        },
      ];
}
