import { z } from "zod";

// Spec 0003 SEC-2: Zod never compiles code at runtime, so the CSP needs no unsafe-eval. Zod reads this setting when an
// object schema is created, not only when it parses, and a module's imports run before its body, so a call in
// main.tsx alone comes too late. Every module that builds schemas when it loads imports `z` from here instead.
z.config({ jitless: true });

export { z };
