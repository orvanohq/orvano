import type { Metadata } from "next";

import "@fontsource/ibm-plex-sans/latin-400.css";
import "@fontsource/ibm-plex-sans/latin-500.css";
import "@fontsource/ibm-plex-sans/latin-600.css";
import "@fontsource/ibm-plex-mono/latin-400.css";
import "@fontsource/ibm-plex-mono/latin-500.css";
import "@fontsource/ibm-plex-mono/latin-600.css";
import "./globals.css";

import { Providers } from "@/shell/providers";
import { Rail } from "@/shell/rail";
import { SkipLink } from "@/shell/skip-link";
import { TopBar } from "@/shell/top-bar";

export const metadata: Metadata = {
  title: { template: "%s · Orvano", default: "Orvano" },
  description: "The Orvano console.",
};

/**
 * Every page shares the skip link, the top bar, the rail slot and `main#main` (spec 0003, "Layout ownership").
 * The rail is picked from the path, so the prerendered HTML already has the right one.
 */
export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    // next-themes sets data-theme before React hydrates.
    <html lang="en" suppressHydrationWarning>
      <body>
        <SkipLink />
        <Providers>
          <div className="flex min-h-svh flex-col">
            <TopBar />
            <div className="flex flex-1">
              <Rail />
              <main
                id="main"
                tabIndex={-1}
                className="min-w-0 flex-1 p-6 outline-none"
              >
                {children}
              </main>
            </div>
          </div>
        </Providers>
      </body>
    </html>
  );
}
