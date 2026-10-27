import { commands } from "vitest/browser";

declare module "vitest/browser" {
  interface BrowserCommands {
    emulateForcedColors: (active: boolean) => Promise<void>;
  }
}

/** Turns forced colors mode on or off for the test page (Playwright `emulateMedia`, see vitest.config.ts). */
export async function emulateForcedColors(active: boolean): Promise<void> {
  await commands.emulateForcedColors(active);
}
