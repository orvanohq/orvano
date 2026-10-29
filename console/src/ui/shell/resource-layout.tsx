import { useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { useLingui } from "@lingui/react/macro";
import { useMove } from "react-aria";
import { resourceListLayout } from "@/shared/storage/items";
import { IconButton } from "../button";
import { cx } from "../cx";
import { CollapseStartIcon, ExpandEndIcon } from "../icons";
import { Drawer } from "./drawer";
import { useShellContext } from "./shell-context";

/** SHL-13, UX-2: the list's limits, the key step, and the content it must leave. */
export const LIST_MIN = 200;
export const LIST_MAX = 480;
export const LIST_STEP = 16;
export const CONTENT_MIN = 480;

export interface ResourceLayoutProps {
  /** The service, part of the storage key (`orvano:v1:{userId}:{projectId}:svc:{service}`). */
  service: string;
  /** Names the list's `nav`, its heading, and its narrow drawer ("Tables"). */
  label: string;
  /** Names the separator ("Resize table list"). */
  resizeLabel: string;
  /** The `ResourceList`. It stays mounted while a resource is open. */
  children: ReactNode;
}

type LayoutIds = Parameters<typeof resourceListLayout.read>[0];

const sameIds = (a: LayoutIds, b: LayoutIds) =>
  a.userId === b.userId && a.projectId === b.projectId && a.service === b.service;

const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max);

/**
 * +1 when moving right points toward the content, -1 in right to left. Read from the layout when the user acts, not
 * from React Aria's locale: engines differ on a locale's direction (CI WebKit gives ar-XB as left to right), while
 * the layout always follows `dir`.
 */
const towardContent = (element: Element) =>
  getComputedStyle(element).direction === "rtl" ? -1 : 1;

/**
 * Spec 0015 SHL-13, UX-2: the resource list's pane, passed as AppShell's `resourceList` so it sits between the
 * sidebar and `main`. From 64rem it is a resizable column (pointer through `useMove`, keyboard on the separator)
 * that collapses to a 40 px strip; below 64rem it is a drawer opened by AppShell's "Show list" button. Width and
 * collapsed state are saved per service when a drag ends or a key changes them, never during a drag.
 */
export function ResourceLayout({ service, label, resizeLabel, children }: ResourceLayoutProps) {
  const { t } = useLingui();
  const shell = useShellContext();
  const ids = { userId: shell.userId, projectId: shell.projectId, service };
  const [loaded, setLoaded] = useState(() => ({ ids, layout: resourceListLayout.read(ids) }));
  const [dragWidth, setDragWidth] = useState<number | null>(null);
  // STO-2: one layout route can keep this element mounted while the service or project changes, so the saved layout
  // is re-read when its key does. Adjusted during render, not in an effect: the old width never paints or saves
  // under the new key, and the separator stays the same element, so it keeps focus.
  let saved = loaded.layout;
  if (!sameIds(loaded.ids, ids)) {
    saved = resourceListLayout.read(ids);
    setLoaded({ ids, layout: saved });
    setDragWidth(null);
  }
  const { attach: attachMax, max } = useListMax();
  const width = clamp(dragWidth ?? saved.width, LIST_MIN, max);

  const save = (next: { collapsed?: boolean; width?: number }) => {
    const value = { ...saved, ...next };
    setLoaded({ ids, layout: value });
    resourceListLayout.write(ids, value);
  };

  const list = (
    <nav aria-label={label} className="flex min-h-0 min-w-0 flex-1 flex-col">
      {children}
    </nav>
  );

  if (!shell.isWide) {
    return (
      <Drawer
        isOpen={shell.listDrawerOpen}
        onOpenChange={shell.setListDrawerOpen}
        aria-label={label}
        width="resource-list"
      >
        {list}
      </Drawer>
    );
  }

  if (saved.collapsed) {
    return (
      <div
        ref={attachMax}
        className="flex w-list-strip shrink-0 justify-center border-e bg-bg-subtle py-2"
      >
        <IconButton
          icon={ExpandEndIcon}
          flipInRtl
          size="sm"
          aria-label={t`Expand list`}
          onPress={() => {
            save({ collapsed: false });
          }}
        />
      </div>
    );
  }

  return (
    <div
      ref={attachMax}
      style={{ width: `${String(width)}px` }}
      className="flex shrink-0 border-e bg-bg-subtle"
    >
      <div className="flex min-w-0 flex-1 flex-col">
        <div className="flex h-control-lg shrink-0 items-center justify-between gap-2 ps-3 pe-1">
          <h2 className="truncate text-sm font-semibold text-fg-muted">{label}</h2>
          <IconButton
            icon={CollapseStartIcon}
            flipInRtl
            size="sm"
            aria-label={t`Collapse list`}
            onPress={() => {
              save({ collapsed: true });
            }}
          />
        </div>
        {list}
      </div>
      <ResizeSeparator
        label={resizeLabel}
        width={width}
        max={max}
        onDrag={setDragWidth}
        onCommit={(next) => {
          setDragWidth(null);
          save({ width: next });
        }}
      />
    </div>
  );
}

/**
 * The maximum: the lower of 480 px and the width that leaves 480 px of content (UX-3). The pane's parent holds the
 * pane and `main`, so its width is what they share; a ResizeObserver follows window and sidebar changes.
 */
function useListMax() {
  const [node, attach] = useState<HTMLDivElement | null>(null);
  const [value, setValue] = useState(LIST_MAX);
  useEffect(() => {
    const parent = node?.parentElement;
    if (!parent) return;
    const observer = new ResizeObserver(() => {
      setValue(clamp(parent.clientWidth - CONTENT_MIN, LIST_MIN, LIST_MAX));
    });
    observer.observe(parent);
    return () => {
      observer.disconnect();
    };
  }, [node]);
  return { attach, max: value };
}

/**
 * SHL-13: a focusable `role="separator"`. Pointer drags move it through `useMove`; ArrowLeft and ArrowRight change
 * the width by 16 px (the arrow pointing toward the content grows the list, so they swap in right to left), and
 * Home and End jump to the limits.
 */
function ResizeSeparator({
  label,
  width,
  max,
  onDrag,
  onCommit,
}: {
  label: string;
  width: number;
  max: number;
  onDrag: (width: number) => void;
  onCommit: (width: number) => void;
}) {
  const separator = useRef<HTMLDivElement>(null);
  // The width during a drag, unclamped, so a drag past a limit and back tracks the pointer.
  const drag = useRef(0);
  const { moveProps } = useMove({
    onMoveStart: () => {
      drag.current = width;
    },
    onMove: (event) => {
      if (event.pointerType === "keyboard") return;
      drag.current += event.deltaX * (separator.current ? towardContent(separator.current) : 1);
      onDrag(clamp(drag.current, LIST_MIN, max));
    },
    onMoveEnd: (event) => {
      if (event.pointerType !== "keyboard") onCommit(clamp(drag.current, LIST_MIN, max));
    },
  });

  const onKeyDown = (event: KeyboardEvent) => {
    const step = LIST_STEP * towardContent(event.currentTarget);
    const keys: Record<string, number> = {
      ArrowRight: clamp(width + step, LIST_MIN, max),
      ArrowLeft: clamp(width - step, LIST_MIN, max),
      Home: LIST_MIN,
      End: max,
    };
    const next = keys[event.key];
    if (next === undefined) return;
    event.preventDefault();
    onCommit(next);
  };

  return (
    <div
      {...moveProps}
      ref={separator}
      onKeyDown={onKeyDown}
      // oxlint-disable-next-line jsx-a11y/prefer-tag-over-role -- SHL-13: the APG window splitter is a focusable separator with a value, which an hr can't hold
      role="separator"
      tabIndex={0}
      aria-orientation="vertical"
      aria-label={label}
      aria-valuenow={width}
      aria-valuemin={LIST_MIN}
      aria-valuemax={max}
      // A 4 px line with a transparent 24 px hit area (UX-32, WCAG 2.5.8): the `before:` box takes the pointer. It
      // reaches 4 px into the list (its end padding, so it stops at the Collapse button) and 16 px into the content.
      className={cx(
        "relative w-1 shrink-0 cursor-col-resize touch-none bg-transparent outline-none hover:bg-accent focus-visible:bg-accent",
        "before:absolute before:inset-y-0 before:-start-1 before:-end-4",
        "focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-solid focus-visible:outline-focus",
      )}
    />
  );
}
