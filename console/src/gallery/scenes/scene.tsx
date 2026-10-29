import { useId, type ReactNode } from "react";
import { useDocumentTitle } from "@/ui";
import { useGalleryText } from "../pseudo";

// The pieces every scene shares: its title, its main, and its demo sections.

/** GAL-2: each scene sets its own document title, `Primitives · Gallery · Orvano` (UX-4). */
export function useSceneTitle(name: string) {
  useDocumentTitle(
    [
      { kind: "page", label: "Gallery" },
      { kind: "page", label: name },
    ],
    {},
  );
}

/** A scene page: its title in the tab and as the h1, then its sections. */
export function Scene({ title, children }: { title: string; children: ReactNode }) {
  const g = useGalleryText();
  useSceneTitle(title);
  return (
    <main className="mx-auto flex max-w-content flex-col gap-8 p-6">
      <h1 className="text-xl font-semibold">{g(title)}</h1>
      {children}
    </main>
  );
}

/**
 * One demo, named by a label that is not a heading: the components inside bring their own h2 and h3 (a card's or
 * an empty state's title), and a demo heading at the same level would flatten the outline (design.md _Typography_).
 */
export function Section({ title, children }: { title: string; children: ReactNode }) {
  const g = useGalleryText();
  const labelId = useId();
  return (
    <section aria-labelledby={labelId} className="flex flex-col gap-3">
      <p id={labelId} className="text-lg font-semibold">
        {g(title)}
      </p>
      {children}
    </section>
  );
}

/** A row of variants, labeled by the prop value it shows (not translated: it is code). */
export function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      <span className="w-24 shrink-0 font-mono text-sm text-fg-muted">{label}</span>
      {children}
    </div>
  );
}
