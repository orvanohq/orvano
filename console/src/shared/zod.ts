import * as z from "zod/mini";

// Spec 0003 SEC-2: Zod never compiles code at runtime, so the CSP needs no unsafe-eval. Zod reads this setting when an
// object schema is created, not only when it parses, and a module's imports run before its body, so a call in
// main.tsx alone comes too late. Every module that builds schemas imports `z` from here instead.
// Spec 0015 STY-8: the console uses Zod only through its `zod/mini` API, and this is the only module importing `zod`.
z.config({ jitless: true });

export { z };
