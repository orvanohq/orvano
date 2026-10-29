import { useEffect, useEffectEvent, useState, type RefObject } from "react";
import { plural } from "@lingui/core/macro";
import { useLingui } from "@lingui/react/macro";
import { useRouter } from "@tanstack/react-router";
import {
  Autocomplete,
  Button as AriaButton,
  Dialog,
  DialogTrigger,
  Input,
  ListBox,
  ListBoxItem,
  SearchField,
} from "react-aria-components";
import { announce } from "../announcer";
import { Button } from "../button";
import { cx, focusRing, focusRingInset } from "../cx";
import { Icon } from "../icon";
import { AddIcon, CheckIcon, DisclosureIcon, SearchIcon } from "../icons";
import { Link } from "../link";
import { Popover } from "../popover";
import { linkOptions } from "./link-options";
import type { ShellLinkOptions, SwitcherItem, SwitcherSearch } from "./types";

/** SHL-12: each search asks for at most this many results. */
export const SWITCHER_LIMIT = 20;
/** SHL-12: a typed query runs this long after the last keystroke. */
export const SWITCHER_DEBOUNCE_MS = 150;

type Kind = "org" | "project";

export interface SwitcherProps {
  /** The current org or project name, shown on the trigger. */
  name: string;
  /** The current org or project id: its result shows a check and `aria-current`, and the highlight starts on it. */
  currentId?: string;
  search: SwitcherSearch;
  links?: { all: ShellLinkOptions; create?: ShellLinkOptions };
}

/** Controlled use from the collapsed breadcrumb menu: the popover anchors to that menu's button. */
export interface SwitcherPopoverProps extends SwitcherProps {
  kind: Kind;
  isOpen?: boolean;
  onOpenChange?: (isOpen: boolean) => void;
  triggerRef?: RefObject<HTMLElement | null>;
}

type Result =
  | { status: "loading"; items: readonly SwitcherItem[] }
  | { status: "done"; items: readonly SwitcherItem[] }
  | { status: "error"; items: readonly SwitcherItem[] };

function useStrings(kind: Kind) {
  const { t } = useLingui();
  return kind === "org"
    ? {
        dialog: t`Switch organization`,
        trigger: (name: string) => t`Switch organization: ${name}`,
        search: t`Search organizations`,
        list: t`Organizations`,
        loading: t`Loading organizations…`,
        empty: t`No organizations match`,
        error: t`Couldn't load organizations. Check your connection and try again.`,
        all: t`All organizations`,
        create: t`New organization`,
        count: (count: number) =>
          t`${plural(count, { one: "# organization", other: "# organizations" })}`,
      }
    : {
        dialog: t`Switch project`,
        trigger: (name: string) => t`Switch project: ${name}`,
        search: t`Search projects`,
        list: t`Projects`,
        loading: t`Loading projects…`,
        empty: t`No projects match`,
        error: t`Couldn't load projects. Check your connection and try again.`,
        all: t`All projects`,
        create: t`New project`,
        count: (count: number) => t`${plural(count, { one: "# project", other: "# projects" })}`,
      };
}

/**
 * Spec 0015 SHL-12: the org or project crumb's button, which opens the switcher. Its name is the visible name plus
 * what it does ("Switch project: Storefront").
 */
function Switcher({
  kind,
  isCurrent,
  ...props
}: SwitcherProps & { kind: Kind; isCurrent?: boolean }) {
  const strings = useStrings(kind);
  return (
    <DialogTrigger>
      <AriaButton
        aria-label={strings.trigger(props.name)}
        // AC-9: the last crumb is the current page, also when it is the org or project.
        aria-current={isCurrent ? "page" : undefined}
        className={cx(
          // SHL-4: never narrower than a 24 px target, however much the name truncates.
          "inline-flex h-control max-w-48 min-w-6 cursor-default items-center gap-1 rounded-md px-2 font-medium text-fg data-hovered:bg-bg-muted data-pressed:bg-bg-muted",
          focusRing,
        )}
      >
        <span className="truncate">{props.name}</span>
        <Icon icon={DisclosureIcon} />
      </AriaButton>
      <SwitcherPopover kind={kind} {...props} />
    </DialogTrigger>
  );
}

export interface SwitcherTriggerProps extends SwitcherProps {
  /** The crumb is the last one, so the trigger is `aria-current="page"`. */
  isCurrent?: boolean;
}

export function OrgSwitcher(props: SwitcherTriggerProps) {
  return <Switcher kind="org" {...props} />;
}

export function ProjectSwitcher(props: SwitcherTriggerProps) {
  return <Switcher kind="project" {...props} />;
}

/** The switcher's popover. Inside a `DialogTrigger`, or controlled with a `triggerRef`. */
export function SwitcherPopover({
  kind,
  isOpen,
  onOpenChange,
  triggerRef,
  ...props
}: SwitcherPopoverProps) {
  const strings = useStrings(kind);
  return (
    <Popover
      placement="bottom start"
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      triggerRef={triggerRef}
    >
      <Dialog aria-label={strings.dialog} className="flex w-72 max-w-full flex-col outline-none">
        {({ close }) => <SwitcherPanel kind={kind} {...props} close={close} />}
      </Dialog>
    </Popover>
  );
}

/**
 * The search box, the results, and the links. It mounts when the popover opens, so the empty query runs at once;
 * typed queries wait 150 ms, and each call aborts the one before it.
 */
function SwitcherPanel({
  kind,
  search,
  currentId,
  links,
  close,
}: SwitcherProps & { kind: Kind; close: () => void }) {
  const strings = useStrings(kind);
  const { t } = useLingui();
  const router = useRouter();
  // The latest `search` without making it a dependency: a caller that passes a new function each render must not
  // start a new search (R-10).
  const runSearch = useEffectEvent((query: string, signal: AbortSignal) =>
    search(query, { limit: SWITCHER_LIMIT, signal }),
  );
  // AC-12: the announcer, not a new live region, says the count, and the error once per failed search.
  const announceCount = useEffectEvent((count: number) => {
    announce(strings.count(count));
  });
  const announceError = useEffectEvent(() => {
    announce(strings.error);
  });
  const [query, setQuery] = useState("");
  // Opening and Retry run at once; typing waits for the debounce.
  const [request, setRequest] = useState({ attempt: 0, immediate: true });
  const [result, setResult] = useState<Result>({ status: "loading", items: [] });

  useEffect(() => {
    const controller = new AbortController();
    const run = async () => {
      try {
        const items = await runSearch(query, controller.signal);
        if (controller.signal.aborted) return;
        setResult({ status: "done", items });
        announceCount(items.length);
      } catch {
        if (controller.signal.aborted) return;
        setResult((previous) => ({ status: "error", items: previous.items }));
        announceError();
      }
    };
    const timer = setTimeout(
      () => {
        void run();
      },
      request.immediate ? 0 : SWITCHER_DEBOUNCE_MS,
    );
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [query, request]);

  return (
    <div className="flex flex-col">
      <Autocomplete
        inputValue={query}
        onInputChange={(value) => {
          setQuery(value);
          setRequest((previous) => ({ attempt: previous.attempt, immediate: false }));
          setResult((previous) => ({ status: "loading", items: previous.items }));
        }}
      >
        <SearchField
          aria-label={strings.search}
          // oxlint-disable-next-line jsx-a11y/no-autofocus -- UX-35: opening the switcher puts focus in its search box, as the APG combobox in a dialog does
          autoFocus
          className="flex items-center gap-2 border-b px-3"
        >
          <Icon icon={SearchIcon} />
          <Input
            placeholder={strings.search}
            className="h-control-lg min-w-0 flex-1 bg-transparent text-fg outline-none [&::-webkit-search-cancel-button]:hidden"
          />
        </SearchField>
        <ListBox
          aria-label={strings.list}
          aria-busy={result.status === "loading" || undefined}
          items={result.items}
          // The current org or project is selected, so the highlight starts on it once the results arrive (SHL-12).
          // Selection never changes here: pressing an item navigates.
          selectionMode={currentId === undefined ? undefined : "single"}
          selectedKeys={currentId === undefined ? undefined : [currentId]}
          disallowEmptySelection
          // oxlint-disable-next-line jsx-a11y/no-autofocus -- SHL-12: virtual focus only (the search box keeps DOM focus); it starts the highlight on the current item
          autoFocus
          onAction={(key) => {
            const item = result.items.find((candidate) => candidate.id === key);
            if (!item) return;
            close();
            void router.navigate(linkOptions(item.link));
          }}
          // The error shows below the list, not as a list item.
          renderEmptyState={
            result.status === "error"
              ? undefined
              : () => (
                  <p className="px-3 py-2 text-fg-muted">
                    {result.status === "loading" ? strings.loading : strings.empty}
                  </p>
                )
          }
          className="max-h-72 overflow-y-auto p-1 outline-none"
        >
          {(item) => (
            <ListBoxItem
              id={item.id}
              textValue={item.name}
              // React Aria's ListBoxItem drops aria-current from its props, so it is set on the element.
              ref={(element) => {
                if (item.id === currentId) element?.setAttribute("aria-current", "true");
                else element?.removeAttribute("aria-current");
              }}
              className={cx(
                "flex h-control items-center gap-2 rounded-md px-2 text-fg data-focused:bg-bg-muted data-hovered:bg-bg-muted",
                focusRingInset,
              )}
            >
              <span className="min-w-0 flex-1 truncate">{item.name}</span>
              {item.id === currentId && <Icon icon={CheckIcon} />}
            </ListBoxItem>
          )}
        </ListBox>
      </Autocomplete>
      {result.status === "error" && (
        <div className="flex flex-col items-start gap-2 border-t px-3 py-2">
          <p className="text-sm text-danger-text">{strings.error}</p>
          <Button
            size="sm"
            onPress={() => {
              setResult((previous) => ({ status: "loading", items: previous.items }));
              setRequest((previous) => ({ attempt: previous.attempt + 1, immediate: true }));
            }}
          >
            {t`Retry`}
          </Button>
        </div>
      )}
      {links && (
        <div className="flex flex-col gap-0.5 border-t p-1">
          <Link variant="sidebar" {...linkOptions(links.all)} onPress={close}>
            {strings.all}
          </Link>
          {links.create && (
            <Link variant="sidebar" {...linkOptions(links.create)} onPress={close}>
              <Icon icon={AddIcon} />
              {strings.create}
            </Link>
          )}
        </div>
      )}
    </div>
  );
}
