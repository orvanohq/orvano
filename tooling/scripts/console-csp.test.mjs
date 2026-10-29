// Spec 0006 NET-2, spec 0015 STY-6: `vite preview` sends the console's CSP so the CSP test runs under the real
// policy. console/vite.config.ts keeps a copy of the server's policy, so this test fails when the two drift apart.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, test } from "node:test";

const root = join(import.meta.dirname, "..", "..");
/** @param {string} path */
const read = (path) => readFileSync(join(root, path), "utf8");

/**
 * The string a constant is set to, joining the literals of a `"a" + "b"` concatenation.
 * @param {string} source
 * @param {RegExp} declaration matches up to the `=` of the constant
 */
export function constantValue(source, declaration) {
  const start = source.search(declaration);
  if (start === -1) throw new Error(`no ${declaration} in the source`);
  const statement = source.slice(start, source.indexOf(";\n", start));
  const literals = [...statement.matchAll(/"((?:[^"\\]|\\.)*)"/g)].map((match) => match[1]);
  if (literals.length === 0) throw new Error(`${declaration} has no string literal`);
  return literals.join("");
}

/** @param {string} [source] */
const serverPolicy = (source = read("server/src/Orvano.Host/Web/SecurityHeaders.cs")) =>
  constantValue(source, /private const string ConsolePolicy\s*=/);
/** @param {string} [source] */
const vitePolicy = (source = read("console/vite.config.ts")) =>
  constantValue(source, /const consolePolicy\s*=/);

describe("the console CSP", () => {
  test("vite.config.ts sends the exact policy SecurityHeaders.cs sends", () => {
    assert.equal(vitePolicy(), serverPolicy());
    assert.match(serverPolicy(), /^default-src 'self'; script-src 'self';/);
  });

  test("fails when one side changes", () => {
    const vite = read("console/vite.config.ts");
    const loosened = vite.replace("script-src 'self'", "script-src 'self' 'unsafe-inline'");
    assert.notEqual(loosened, vite);
    assert.notEqual(vitePolicy(loosened), serverPolicy());

    const server = read("server/src/Orvano.Host/Web/SecurityHeaders.cs");
    const dropped = server.replace(" frame-ancestors 'none'", "");
    assert.notEqual(dropped, server);
    assert.notEqual(vitePolicy(), serverPolicy(dropped));
  });

  test("reads every literal of a concatenated constant", () => {
    assert.equal(constantValue('const a =\n  "x; " +\n  "y";\n', /const a\s*=/), "x; y");
    assert.throws(() => constantValue("const b = 1;\n", /const a\s*=/), /no/);
  });
});
