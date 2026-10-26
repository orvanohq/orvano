import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { createHashHistory, RouterProvider } from "@tanstack/react-router";
import { z } from "zod";
import { startThemeSync } from "@/shared/stores/theme";
import { GalleryProviders } from "./providers";
import { createGalleryRouter } from "./router";
import { inFrame } from "./settings";
import "@/ui/theme.css"; // spec 0015 STY-1: the same stylesheet as the app

// Spec 0015 GAL-1: the development gallery's entry, served by `pnpm dev` at /console/gallery.html.
z.config({ jitless: true });
startThemeSync();

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
