import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { createHashHistory, RouterProvider } from "@tanstack/react-router";
import { z } from "@/shared/zod";
import { applyStoredDensity } from "@/shared/stores/layout";
import { startThemeSync } from "@/shared/stores/theme";
import { user } from "./fixtures";
import { GalleryProviders } from "./providers";
import { createGalleryRouter } from "./router";
import { inFrame } from "./settings";
import "@/ui/theme.css"; // spec 0015 STY-1: the same stylesheet as the app

// Spec 0015 GAL-1: the development gallery's entry, served by `pnpm dev` at /console/gallery.html.
z.config({ jitless: true });
startThemeSync();
applyStoredDensity(user.id); // SHL-11: the gallery's stand in for feature 13's boot step

const rootElement = document.getElementById("root");
if (!rootElement) throw new Error("gallery.html has no #root element");

const router = createGalleryRouter({ history: createHashHistory(), toolbar: !inFrame });

createRoot(rootElement).render(
  <StrictMode>
    <GalleryProviders>
      <RouterProvider router={router} />
    </GalleryProviders>
  </StrictMode>,
);
