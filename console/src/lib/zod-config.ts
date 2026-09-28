import { z } from 'zod'

// Zod probes `new Function` when it builds an object schema, to decide whether to compile a fast
// parser. The production CSP blocks eval, and the browser reports the blocked probe as a violation
// even though Zod catches it (spec 0005, AC-24). `jitless` skips the probe. `main.tsx` imports this
// module first, so it runs before any module builds a schema.
z.config({ jitless: true })
