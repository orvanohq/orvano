// Spec 0010 AC-20 throwaway proof: a deliberate command injection, so CodeQL raises a high alert. Never merge.
import { exec } from "node:child_process";
import { createServer } from "node:http";

createServer((request, response) => {
  const dir = new URL(request.url ?? "/", "http://localhost").searchParams.get("dir") ?? ".";
  exec(`ls ${dir}`, (_error, stdout) => {
    response.end(stdout);
  });
}).listen(8080);
