// Spec 0014 JSW-5, CLT-2: a format int64 value is either a JSON number with a maximum at or below 2^53 - 1, or a
// string with a pattern, so every SDK can read it without losing precision.
const maxSafe = 9007199254740991;

export default function orvanoInt64(schema) {
  if (schema === null || typeof schema !== "object" || schema.format !== "int64") return [];
  const types = Array.isArray(schema.type) ? schema.type : [schema.type];
  if (types.includes("string")) {
    return typeof schema.pattern === "string"
      ? []
      : [{ message: "An int64 string needs a pattern, such as ^-?[0-9]+$." }];
  }
  if (typeof schema.maximum === "number" && schema.maximum <= maxSafe) return [];
  return [
    {
      message: `An int64 number needs a maximum at or below ${maxSafe}; a value that can exceed it is a string.`,
    },
  ];
}
