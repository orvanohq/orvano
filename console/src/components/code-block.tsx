"use client";

import { Check, Copy } from "lucide-react";
import { useEffect, useState } from "react";

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
 */
export function CodeBlock({
  code,
  language,
}: {
  code: string;
  language: string;
}) {
  const [state, setState] = useState<CopyState>("idle");

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
      <pre className="overflow-x-auto p-3 font-mono text-mono text-ink">
        <code>{code}</code>
      </pre>
      <span role="status" aria-live="polite" className="sr-only">
        {announcement[state]}
      </span>
    </figure>
  );
}
