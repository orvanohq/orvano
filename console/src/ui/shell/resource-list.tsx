import { useEffect, useEffectEvent, useState } from "react";
import { plural } from "@lingui/core/macro";
import { useLingui } from "@lingui/react/macro";
import { Input, SearchField } from "react-aria-components";
import { announce } from "../announcer";
import { IconButton } from "../button";
import { AddIcon, SearchIcon } from "../icons";
import { Icon } from "../icon";
import { Link } from "../link";
import { ContentSkeleton, useDelayedPending } from "../skeleton";
import { Tooltip } from "../tooltip";
import { linkOptions } from "./link-options";
import type { ShellLinkOptions } from "./types";

export interface ResourceListItem {
  id: string;
  name: string;
  link: ShellLinkOptions;
}

export interface ResourceListProps {
  /** The resources, or `undefined` while they load. */
  items: readonly ResourceListItem[] | undefined;
  /** Names the filter box ("Filter tables"). */
  filterLabel: string;
  /** Shown when the service has none yet ("No tables yet"). */
  emptyMessage: string;
  /** The create button's label ("Create table"); the button shows when `onCreate` is given. */
  createLabel?: string;
  onCreate?: () => void;
}

/** How long typing must pause before the filter's match count is announced, so each key press isn't read out. */
export const FILTER_SETTLE_MS = 500;

/**
 * Spec 0001 UX-2: a filter box and a create button on top, then the resources as links, the open one
 * `aria-current="page"`. Links don't preload (RTE-6) and keep focus on navigation (RTE-7). Long names truncate,
 * keeping the full accessible name, with the name in a tooltip (UX-40). Once typing settles, the announcer says how
 * many match (UX-41). While the resources load the list is `aria-busy`, with a skeleton after 300 ms (UX-21).
 */
export function ResourceList({
  items,
  filterLabel,
  emptyMessage,
  createLabel,
  onCreate,
}: ResourceListProps) {
  const { t, i18n } = useLingui();
  const [filter, setFilter] = useState("");
  // The active locale's case rules: in Turkish "İ" lower cases to "i" and "I" to "ı".
  const lower = (text: string) => text.toLocaleLowerCase(i18n.locale);
  const query = lower(filter.trim());
  const shown = items?.filter((item) => lower(item.name).includes(query));
  const showSkeleton = useDelayedPending(items === undefined);

  const announceCount = useEffectEvent(() => {
    if (shown === undefined) return;
    const count = shown.length;
    announce(t`${plural(count, { one: "# match", other: "# matches" })}`);
  });
  // Counts the user's edits, so only typing is announced, never the first render. Each edit restarts the wait.
  const [edits, setEdits] = useState(0);
  useEffect(() => {
    if (edits === 0) return undefined;
    const timer = setTimeout(() => {
      announceCount();
    }, FILTER_SETTLE_MS);
    return () => {
      clearTimeout(timer);
    };
  }, [edits]);

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-2 p-2">
      <div className="flex items-center gap-1">
        <SearchField
          aria-label={filterLabel}
          value={filter}
          onChange={(value) => {
            setFilter(value);
            setEdits((count) => count + 1);
          }}
          className="flex h-control min-w-0 flex-1 items-center gap-2 rounded-md border border-border-strong bg-bg px-2 has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-solid has-focus-visible:outline-focus"
        >
          <Icon icon={SearchIcon} />
          <Input
            placeholder={filterLabel}
            className="min-w-0 flex-1 bg-transparent text-fg outline-none [&::-webkit-search-cancel-button]:hidden"
          />
        </SearchField>
        {onCreate && createLabel && (
          <IconButton
            icon={AddIcon}
            aria-label={createLabel}
            variant="secondary"
            onPress={onCreate}
          />
        )}
      </div>
      {shown === undefined ? (
        <div aria-busy="true" className="px-2">
          {showSkeleton ? <ContentSkeleton /> : null}
        </div>
      ) : shown.length === 0 ? (
        <p className="px-2 text-fg-muted">
          {items?.length === 0 ? emptyMessage : t`Nothing matches “${filter}”`}
        </p>
      ) : (
        <ul className="flex min-h-0 flex-col gap-0.5 overflow-y-auto">
          {shown.map((item) => (
            <li key={item.id} className="flex">
              <Tooltip content={item.name} placement="end">
                <Link
                  variant="resource"
                  {...linkOptions(item.link)}
                  preload={false}
                  state={{ keepFocus: true } as never}
                >
                  <span className="min-w-0 flex-1 truncate">{item.name}</span>
                </Link>
              </Tooltip>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
