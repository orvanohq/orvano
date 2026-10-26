import { z } from "zod";
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
