// Spec 0014 CLT-2: each (x-orvano-sdk.service, x-orvano-sdk.method) pair names one operation, so no SDK method clashes.
const methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

export default function orvanoSdkUnique(paths) {
  const seen = new Map();
  const results = [];
  for (const [path, item] of Object.entries(paths ?? {})) {
    for (const method of methods) {
      const sdk = item?.[method]?.["x-orvano-sdk"];
      if (!sdk?.service || !sdk?.method) continue;
      const key = `${sdk.service}.${sdk.method}`;
      if (seen.has(key)) {
        results.push({
          message: `x-orvano-sdk ${key} is already used by ${seen.get(key)}.`,
          path: ["paths", path, method, "x-orvano-sdk"],
        });
      } else {
        seen.set(key, `${method.toUpperCase()} ${path}`);
      }
    }
  }
  return results;
}
