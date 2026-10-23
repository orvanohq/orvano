// TypeScript rendering helpers shared by the ts-client and ts-server targets (spec 0005 CGN-1, TYP-4).
import { GeneratorError } from "../../contract.ts";
import type { Property, TypeRef } from "../../model.ts";
import { headerLine, type Provenance } from "../../output.ts";

/** The first lines of every generated TypeScript file (GNR-8, spec 0005 CGN-1). */
export function fileHeader(source: Provenance): string {
  return `// @generated\n// ${headerLine(source)}\n`;
}

/** A TSDoc block. Paragraphs are separated by a blank line; tags (`@public`) go last, one per line. */
export function tsDoc(paragraphs: readonly string[], tags: readonly string[], indent = ""): string {
  const lines: string[] = [];
  for (const paragraph of paragraphs.map((text) => text.trim()).filter((text) => text !== "")) {
    if (lines.length > 0) lines.push("");
    lines.push(...escapeDoc(paragraph).split("\n"));
  }
  if (tags.length > 0 && lines.length > 0) lines.push("");
  lines.push(...tags);
  if (lines.length === 0) return "";
  if (lines.length === 1) return `${indent}/** ${lines[0] ?? ""} */\n`;
  return `${indent}/**\n${lines.map((line) => (line === "" ? `${indent} *` : `${indent} * ${line}`)).join("\n")}\n${indent} */\n`;
}

function escapeDoc(text: string): string {
  return text.replace(/\*\//g, "*\\/").replace(/\r\n?/g, "\n");
}

const identifier = /^[A-Za-z_$][A-Za-z0-9_$]*$/;

/** A property key, quoted when it is not a plain identifier. */
export function propertyKey(name: string): string {
  return identifier.test(name) ? name : JSON.stringify(name);
}

/** Server enums are open unions, so a newer server's value never breaks an older SDK (TYP-4, JSW-2). */
const openString = "(string & {})";

export function tsType(type: TypeRef, where: string): string {
  switch (type.kind) {
    case "string":
      return "string";
    case "integer":
    case "number":
      return "number";
    case "boolean":
      return "boolean";
    case "unknown":
      return "unknown";
    case "null":
      return "null";
    case "ref":
      return type.name;
    case "enum":
      return [...type.values.map((value) => JSON.stringify(value)), openString].join(" | ");
    case "array": {
      const items = tsType(type.items, where);
      return isSimple(type.items) ? `${items}[]` : `(${items})[]`;
    }
    case "map":
      return `Record<string, ${tsType(type.values, where)}>`;
    case "object":
      return `{ ${type.properties.map((property) => `${inlineProperty(property, where)};`).join(" ")} }`;
    case "union":
      if (type.members.length === 0) throw new GeneratorError(`${where}: an empty union.`);
      return type.members.map((member) => tsType(member, where)).join(" | ");
    case "nullable":
      return `${tsType(type.inner, where)} | null`;
  }
}

function inlineProperty(property: Property, where: string): string {
  const optional = property.required ? "" : "?";
  return `${propertyKey(property.name)}${optional}: ${tsType(property.type, where)}`;
}

function isSimple(type: TypeRef): boolean {
  return !["enum", "union", "nullable"].includes(type.kind);
}
