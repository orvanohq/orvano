// Dart rendering helpers shared by the dart-client and dart-server targets (spec 0004 MDL-1, MDL-2, DRT-5).
import { GeneratorError } from "../../contract.ts";
import { headerLine, type Provenance } from "../../output.ts";
import type { DartType } from "./types.ts";

/** The first lines of every generated Dart file (GNR-8, spec 0004 MDL-1). */
export function fileHeader(source: Provenance): string {
  return `// GENERATED CODE - DO NOT MODIFY BY HAND\n// ${headerLine(source)}\n`;
}

/** A dartdoc block. Paragraphs are separated by a blank `///` line. */
export function dartDoc(paragraphs: readonly string[], indent = ""): string {
  const lines: string[] = [];
  for (const paragraph of paragraphs.map((text) => text.trim()).filter((text) => text !== "")) {
    if (lines.length > 0) lines.push("");
    for (const line of paragraph.replace(/\r\n?/g, "\n").split("\n"))
      lines.push(...wrap(line, 80 - indent.length - 4));
  }
  return lines.map((line) => (line === "" ? `${indent}///\n` : `${indent}/// ${line}\n`)).join("");
}

/** Splits a line at spaces so each part fits `width` (dart format leaves comments as they are). */
function wrap(line: string, width: number): string[] {
  const parts: string[] = [];
  let current = "";
  for (const word of line.split(" ")) {
    if (current !== "" && current.length + 1 + word.length > width) {
      parts.push(current);
      current = word;
    } else {
      current = current === "" ? word : `${current} ${word}`;
    }
  }
  parts.push(current);
  return parts;
}

/** A Dart string literal: single quoted, raw when it holds a `$` or a backslash. */
export function dartString(value: string): string {
  if (/[\n\r]/.test(value))
    return `'${value.replace(/\\/g, "\\\\").replace(/'/g, "\\'").replace(/\$/g, "\\$").replace(/\n/g, "\\n").replace(/\r/g, "\\r")}'`;
  if (value.includes("'")) {
    return `"${value.replace(/\\/g, "\\\\").replace(/"/g, '\\"').replace(/\$/g, "\\$")}"`;
  }
  return /[$\\]/.test(value) ? `r'${value}'` : `'${value}'`;
}

// https://dart.dev/language/keywords: reserved words, which no identifier may be.
const reservedWords: ReadonlySet<string> = new Set([
  "assert",
  "break",
  "case",
  "catch",
  "class",
  "const",
  "continue",
  "default",
  "do",
  "else",
  "enum",
  "extends",
  "false",
  "final",
  "finally",
  "for",
  "if",
  "in",
  "is",
  "new",
  "null",
  "rethrow",
  "return",
  "super",
  "switch",
  "this",
  "throw",
  "true",
  "try",
  "var",
  "void",
  "while",
  "with",
  // Not reserved, but a member or parameter with these names breaks the generated code.
  "await",
  "yield",
  "hashCode",
  "runtimeType",
  "noSuchMethod",
  "toString",
  "toJson",
  "copyWith",
]);

const identifier = /^[a-z][A-Za-z0-9]*$/;

/** Fails unless `name` can be a Dart member or parameter name as is. */
export function checkMemberName(name: string, where: string): string {
  if (!identifier.test(name) || reservedWords.has(name)) {
    throw new GeneratorError(`${where}: ${name} can't be a Dart member name.`);
  }
  return name;
}

export function dartTypeName(type: DartType): string {
  switch (type.kind) {
    case "string":
      return "String";
    case "int":
      return "int";
    case "double":
      return "double";
    case "bool":
      return "bool";
    case "dateTime":
      return "DateTime";
    case "any":
      return "Object?";
    case "enum":
    case "class":
      return type.name;
    case "list":
      return `List<${dartTypeName(type.items)}>`;
    case "map":
      return `Map<String, ${dartTypeName(type.values)}>`;
    case "nullable":
      return `${dartTypeName(type.inner)}?`;
  }
}

/** The non nullable form of a type. */
export function nonNull(type: DartType): DartType {
  return type.kind === "nullable" ? type.inner : type;
}

const readers: Partial<Record<DartType["kind"], string>> = {
  string: "readString",
  int: "readInt",
  double: "readDouble",
  bool: "readBool",
  dateTime: "readDateTime",
  any: "readAny",
};

/** A function `(Object? value, String path) => T` that reads the type (spec 0004 MDL-2). */
export function readerFunction(type: DartType, depth = 0): string {
  const reader = readers[type.kind];
  if (reader !== undefined) return reader;
  const value = `v${String(depth)}`;
  const path = `p${String(depth)}`;
  return `(${value}, ${path}) => ${readExpression(type, value, path, depth + 1)}`;
}

/** An expression that reads `value` (at the JSON path `path`) as the type. */
export function readExpression(type: DartType, value: string, path: string, depth = 0): string {
  switch (type.kind) {
    case "enum":
      return `${type.name}.fromJson(readString(${value}, ${path}))`;
    case "class":
      return `${type.name}.fromJson(readObject(${value}, ${path}), ${path})`;
    case "list":
      return `readList(${value}, ${path}, ${readerFunction(type.items, depth)})`;
    case "map":
      return `readMap(${value}, ${path}, ${readerFunction(type.values, depth)})`;
    case "nullable":
      return `readNullable(${value}, ${path}, ${readerFunction(type.inner, depth)})`;
    default:
      return `${readers[type.kind] ?? "readAny"}(${value}, ${path})`;
  }
}

/** An expression that writes `value` of the type as JSON. */
export function writeExpression(type: DartType, value: string, depth = 0): string {
  const item = `e${String(depth)}`;
  switch (type.kind) {
    case "dateTime":
      return `writeDateTime(${value})`;
    case "enum":
    case "class":
      return `${value}.toJson()`;
    case "list":
      return isPlain(type.items)
        ? value
        : `[for (final ${item} in ${value}) ${writeExpression(type.items, item, depth + 1)}]`;
    case "map":
      return isPlain(type.values)
        ? value
        : `{for (final MapEntry(key: k${String(depth)}, value: ${item}) in ${value}.entries) k${String(depth)}: ${writeExpression(type.values, item, depth + 1)}}`;
    case "nullable":
      return writeNullable(type.inner, value);
    default:
      return value;
  }
}

/** An expression that writes `value`, which may be null, as JSON or null. */
export function writeNullable(type: DartType, value: string): string {
  const inner = nonNull(type);
  if (isPlain(inner)) return value;
  if (inner.kind === "enum" || inner.kind === "class") return `${value}?.toJson()`;
  return `switch (${value}) { final e0? => ${writeExpression(inner, "e0", 1)}, _ => null }`;
}

/** A type whose Dart value is already its JSON. */
export function isPlain(type: DartType): boolean {
  switch (type.kind) {
    case "string":
    case "int":
    case "double":
    case "bool":
    case "any":
      return true;
    case "list":
      return isPlain(type.items);
    case "map":
      return isPlain(type.values);
    case "nullable":
      return isPlain(type.inner);
    default:
      return false;
  }
}

/** True when equality needs package:collection's deep equality. */
export function isCollection(type: DartType): boolean {
  const inner = nonNull(type);
  return inner.kind === "list" || inner.kind === "map" || inner.kind === "any";
}
