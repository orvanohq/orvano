import { useId, useRef } from "react";
import { useLingui } from "@lingui/react/macro";
import { IconButton } from "./button";
import { useCopy } from "./copy-field";
import { cx } from "./cx";
import { CheckIcon, CopyIcon } from "./icons";

export interface CodeSnippetProps {
  /** What the code is ("Install command"); the copy button is "Copy" plus this label. */
  label: string;
  code: string;
}

/** design.md _Data display_: plain monospace code on `bg-muted` with a copy button. No highlighting yet. */
export function CodeSnippet({ label, code }: CodeSnippetProps) {
  const { t } = useLingui();
  const region = useRef<HTMLElement>(null);
  const pre = useRef<HTMLPreElement>(null);
  const hintId = useId();
  const { state, copy, hint } = useCopy();
  return (
    <div className="flex flex-col gap-1.5">
      <div className="relative min-w-0 rounded-md bg-bg-muted">
        {/* Focusable, so the code can be scrolled sideways by keyboard; a named region, so focus lands on its label. */}
        <section
          ref={region}
          aria-label={label}
          aria-describedby={state === "manual" ? hintId : undefined}
          // oxlint-disable-next-line jsx-a11y/no-noninteractive-tabindex -- a scrollable region must be reachable by keyboard (WCAG 2.1.1)
          tabIndex={0}
          className={cx(
            "overflow-x-auto rounded-md p-3 pe-12 outline-none",
            "focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-solid focus-visible:outline-focus",
          )}
        >
          <pre ref={pre} className="font-mono text-sm text-fg">
            <code>{code}</code>
          </pre>
        </section>
        <div className="absolute end-2 top-2">
          <IconButton
            size="sm"
            icon={state === "copied" ? CheckIcon : CopyIcon}
            aria-label={t`Copy ${label}`}
            onPress={() => {
              void copy(code, () => {
                if (!region.current || !pre.current) return;
                region.current.focus();
                // The code itself, not its region: a selected block would add a line break in some engines.
                window.getSelection()?.selectAllChildren(pre.current);
              });
            }}
          />
        </div>
      </div>
      {state === "manual" ? (
        <p id={hintId} className="text-sm text-fg-muted">
          {hint}
        </p>
      ) : null}
    </div>
  );
}
