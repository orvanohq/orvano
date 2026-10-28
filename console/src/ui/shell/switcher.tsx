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
import { AddIcon, DisclosureIcon, SearchIcon } from "../icons";
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
function Switcher({ kind, ...props }: SwitcherProps & { kind: Kind }) {
  const strings = useStrings(kind);
  return (
    <DialogTrigger>
      <AriaButton
        aria-label={strings.trigger(props.name)}
        className={cx(
          "inline-flex h-control max-w-48 min-w-0 cursor-default items-center gap-1 rounded-md px-2 font-medium text-fg data-hovered:bg-bg-muted data-pressed:bg-bg-muted",
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

export function OrgSwitcher(props: SwitcherProps) {
  return <Switcher kind="org" {...props} />;
}

export function ProjectSwitcher(props: SwitcherProps) {
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
  links,
  close,
}: SwitcherProps & { kind: Kind; close: () => void }) {
  const strings = useStrings(kind);
  const { t } = useLingui();
  const router = useRouter();
  const announceCount = useEffectEvent((count: number) => {
    announce(strings.count(count));
  });
  const [query, setQuery] = useState("");
  // Opening and Retry run at once; typing waits for the debounce.
  const [request, setRequest] = useState({ attempt: 0, immediate: true });
  const [result, setResult] = useState<Result>({ status: "loading", items: [] });

  useEffect(() => {
    const controller = new AbortController();
    const run = async () => {
      try {
        const items = await search(query, { limit: SWITCHER_LIMIT, signal: controller.signal });
        if (controller.signal.aborted) return;
        setResult({ status: "done", items });
        announceCount(items.length);
      } catch {
        if (controller.signal.aborted) return;
        setResult((previous) => ({ status: "error", items: previous.items }));
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
  }, [search, query, request]);

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
              className={cx(
                "flex h-control items-center rounded-md px-2 text-fg data-focused:bg-bg-muted data-hovered:bg-bg-muted",
                focusRingInset,
              )}
            >
              <span className="truncate">{item.name}</span>
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
