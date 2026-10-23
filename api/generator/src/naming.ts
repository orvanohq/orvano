// Name conversions every target shares. Contract names are camelCase (x-orvano-sdk), snake_case (error codes),
// or Hyphen-Case (headers); each target turns the words into its own style.

/** Splits a name into lowercase words: `listAll`, `list_all`, `List-All`, and `ListAll` all give `list`, `all`. */
export function words(name: string): string[] {
  return name
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .split(/[^A-Za-z0-9]+/)
    .filter((word) => word.length > 0)
    .map((word) => word.toLowerCase());
}

export function pascalCase(name: string): string {
  return words(name)
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join("");
}

export function camelCase(name: string): string {
  const pascal = pascalCase(name);
  return pascal.charAt(0).toLowerCase() + pascal.slice(1);
}

export function kebabCase(name: string): string {
  return words(name).join("-");
}

export function snakeCase(name: string): string {
  return words(name).join("_");
}

/** Compares strings by UTF-16 code units, the same on every machine and locale (GNR-8). */
export function byString(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}
