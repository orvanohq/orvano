"use client";

import { Check, Copy } from "lucide-react";
import { useEffect, useRef, useState, type RefObject } from "react";

import { Button } from "@/components/ui/button";

type CopyState = "idle" | "copied" | "failed";

const announcement: Record<CopyState, string> = {
  idle: "",
  copied: "Copied",
  failed: "Copy failed",
};

/**
 * Code in mono on surface-subtle with its language label and a copy button. A polite live region says
 * "Copied" for two seconds, or "Copy failed" when the clipboard is not available.
 * When the code is wider than the block, the code area becomes a labelled tab stop, so keyboard users can
 * scroll it with the arrow keys (WCAG 2.1.1). Code that fits adds no extra tab stop.
 */
export function CodeBlock({
  code,
  language,
}: {
  code: string;
  language: string;
}) {
  const [state, setState] = useState<CopyState>("idle");
  const codeArea = useRef<HTMLPreElement>(null);
  const scrollable = useOverflows(codeArea, code);

  useEffect(() => {
    if (state === "idle") {
      return;
    }
    const timer = window.setTimeout(() => setState("idle"), 2000);
    return () => window.clearTimeout(timer);
  }, [state]);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(code);
      setState("copied");
    } catch {
      setState("failed");
    }
  };

  return (
    <figure
      data-slot="code-block"
      className="relative rounded-md border border-border bg-surface-subtle"
    >
      <figcaption className="flex h-9 items-center justify-between border-b border-border pr-1 pl-3">
        <span className="text-xs text-ink-muted">{language}</span>
        <Button
          variant="ghost"
          size="sm"
          onClick={copy}
          className="text-ink-muted hover:text-ink"
        >
          {state === "copied" ? (
            <Check aria-hidden="true" data-icon="inline-start" />
          ) : (
            <Copy aria-hidden="true" data-icon="inline-start" />
          )}
          {state === "failed" ? "Copy failed" : "Copy code"}
        </Button>
      </figcaption>
      <pre
        ref={codeArea}
        {...(scrollable
          ? { tabIndex: 0, role: "group", "aria-label": `${language} code` }
          : {})}
        className="overflow-x-auto rounded-b-md p-3 font-mono text-mono text-ink outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset"
      >
        <code>{code}</code>
      </pre>
      <span role="status" aria-live="polite" className="sr-only">
        {announcement[state]}
      </span>
    </figure>
  );
}

/** True while the element's content is wider than the element. Measured again on resize and when the content changes. */
function useOverflows(
  ref: RefObject<HTMLElement | null>,
  content: string,
): boolean {
  const [overflows, setOverflows] = useState(false);

  useEffect(() => {
    const element = ref.current;
    if (!element) {
      return;
    }
    const measure = () =>
      setOverflows(element.scrollWidth > element.clientWidth);
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    return () => observer.disconnect();
  }, [ref, content]);

  return overflows;
}
