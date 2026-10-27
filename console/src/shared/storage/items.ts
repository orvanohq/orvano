import { z } from "@/shared/zod";
import { defineStored } from "./stored";

// Spec 0003 STO-2: every stored item, declared once. Feature 11 adds the rest of the table as their stores land.

export const themePreferenceSchema = z.enum(["system", "light", "dark"]);
export type ThemePreference = z.infer<typeof themePreferenceSchema>;

/** Spec 0015 THM-1: read before sign in, so it has no user in its key. public/theme-boot.js reads the same key. */
export const themePreference = defineStored({
  key: () => "orvano:v1:theme",
  schema: themePreferenceSchema,
  fallback: "system",
});

export const densitySchema = z.enum(["comfortable", "compact"]);
export type Density = z.infer<typeof densitySchema>;

/**
 * Spec 0015 SHL-11: the one layout item per user. Each field falls back on its own, so a bad field never wipes the
 * others. `lastSdk` belongs to UX-37's snippet picker, which narrows it when it lands.
 */
export const layout = defineStored({
  key: (ids: { userId: string }) => `orvano:v1:${ids.userId}:layout`,
  schema: z.object({
    sidebarCollapsed: z.boolean().catch(false),
    density: densitySchema.catch("comfortable"),
    lastSdk: z.string().optional().catch(undefined),
  }),
  fallback: { sidebarCollapsed: false, density: "comfortable" },
});
export type Layout = ReturnType<typeof layout.read>;

/** Spec 0015 SHL-13, UX-2: the resource list's width and collapsed state, per service. */
export const resourceListLayout = defineStored({
  key: (ids: { userId: string; projectId: string; service: string }) =>
    `orvano:v1:${ids.userId}:${ids.projectId}:svc:${ids.service}`,
  schema: z.object({
    collapsed: z.boolean().catch(false),
    width: z.number().int().min(200).max(480).catch(280),
  }),
  fallback: { collapsed: false, width: 280 },
});
export type ResourceListLayout = ReturnType<typeof resourceListLayout.read>;

/** Spec 0015 SHL-10, UX-25: the command palette's recent action IDs, newest first, at most 10. */
export const PALETTE_RECENTS_LIMIT = 10;
export const paletteRecents = defineStored({
  key: (ids: { userId: string }) => `orvano:v1:${ids.userId}:palette`,
  schema: z.array(z.string()).max(PALETTE_RECENTS_LIMIT),
  fallback: [],
});
