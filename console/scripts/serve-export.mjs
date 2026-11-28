// Serves console/out at /console with the same rules as the orvano host (spec 0003, "HTTP surface").
// For the Playwright tests and for looking at a build: node scripts/serve-export.mjs [--port 3100]
// The cases and their order follow the spec's table, the same order as the host's ConsoleFilesTests.
import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { createServer } from "node:http";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, "../out");
const portFlag = process.argv.indexOf("--port");
const port = portFlag > 0 ? Number(process.argv[portFlag + 1]) : 3100;
const prefix = "/console";

const types = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript",
  ".css": "text/css",
  ".txt": "text/plain",
  ".woff2": "font/woff2",
  ".woff": "font/woff",
  ".svg": "image/svg+xml",
  ".ico": "image/x-icon",
  ".json": "application/json",
};

const securityHeaders = {
  "X-Content-Type-Options": "nosniff",
  "Referrer-Policy": "strict-origin-when-cross-origin",
  "X-Frame-Options": "DENY",
  "Content-Security-Policy": "frame-ancestors 'none'",
};

const isFile = async (file) =>
  (await stat(file).catch(() => null))?.isFile() ?? false;

/** Resolves a request path inside `out/`, refusing anything that climbs out of it. */
const inRoot = (relative) => {
  const full = path.resolve(root, `.${relative}`);
  return full === root || full.startsWith(`${root}${path.sep}`) ? full : null;
};

const send = (res, status, headers, body) => {
  res.writeHead(status, { ...securityHeaders, ...headers });
  res.end(body);
};

const sendFile = (req, res, status, file, cacheControl) => {
  const type = types[path.extname(file)];
  res.writeHead(status, {
    ...securityHeaders,
    "Content-Type": type,
    "Cache-Control": cacheControl,
  });
  if (req.method === "HEAD") {
    res.end();
  } else {
    createReadStream(file).pipe(res);
  }
};

const server = createServer(async (req, res) => {
  const url = new URL(req.url ?? "/", "http://localhost");
  const pathname = decodeURIComponent(url.pathname);

  // Outside the branch: nothing, and none of the console headers.
  if (pathname !== prefix && !pathname.startsWith(`${prefix}/`)) {
    res.writeHead(404, { "Content-Type": "text/plain" });
    res.end("Not under /console");
    return;
  }
  const relative = pathname.slice(prefix.length);

  // 1. index.html missing: 503 problem.
  if (!(await isFile(path.join(root, "index.html")))) {
    send(
      res,
      503,
      {
        "Content-Type": "application/problem+json",
        "Cache-Control": "no-store",
      },
      JSON.stringify({
        type: "about:blank",
        title: "Console not built",
        status: 503,
        code: "console_not_built",
        hint: "Build it with `pnpm --filter @orvano/console build` and point `Console:RootPath` at `console/out`, or open the console dev server from the AppHost.",
      }),
    );
    return;
  }

  // 2. Methods other than GET and HEAD: 405.
  if (req.method !== "GET" && req.method !== "HEAD") {
    send(res, 405, { Allow: "GET, HEAD", "Cache-Control": "no-cache" });
    return;
  }

  // 3. /console exactly: 308 to /console/.
  if (relative === "") {
    send(res, 308, {
      Location: `${prefix}/${url.search}`,
      "Cache-Control": "no-cache",
    });
    return;
  }

  const target = inRoot(relative);

  // 4. A folder path without its slash, where the folder has an index.html: 301 to the slash form.
  if (
    target &&
    !relative.endsWith("/") &&
    (await isFile(path.join(target, "index.html")))
  ) {
    send(res, 301, {
      Location: `${pathname}/${url.search}`,
      "Cache-Control": "no-cache",
    });
    return;
  }

  // 5 and 6. A file (or a folder's index.html), if its type is known.
  const file =
    target &&
    (relative.endsWith("/") ? path.join(target, "index.html") : target);
  if (file && types[path.extname(file)] && (await isFile(file))) {
    const immutable = relative.startsWith("/_next/static/");
    sendFile(
      req,
      res,
      200,
      file,
      immutable ? "public, max-age=31536000, immutable" : "no-cache",
    );
    return;
  }

  // 7. Anything else: the console's not found page.
  sendFile(req, res, 404, path.join(root, "404.html"), "no-cache");
});

server.listen(port, () => {
  console.log(
    `Serving ${path.relative(process.cwd(), root) || "."} at http://localhost:${port}${prefix}/`,
  );
});
