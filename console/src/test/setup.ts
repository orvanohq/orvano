import { afterEach } from "vitest";
import { z } from "zod";
import "@/ui/theme.css";
import { resetAnnouncerForTests } from "@/ui/announcer";

// Every browser test runs with the app's boot settings: the one stylesheet (so axe checks real colors) and Zod
// without code generation (spec 0003 SEC-2), like main.tsx.
z.config({ jitless: true });

// React Aria's announcer outlives a test, so each test starts without its leftover messages.
afterEach(resetAnnouncerForTests);
