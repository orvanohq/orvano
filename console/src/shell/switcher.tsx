import { Combobox } from '@base-ui/react/combobox'
import { Check, ChevronsUpDown, Loader2 } from 'lucide-react'
import { useEffect, useState, type ReactNode, type Ref } from 'react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { statusLabel } from '@/shell/status'
import type { OrgStatus, ProjectStatus } from '@orvano/console-client'

/** One row of a switcher: an org or a project. */
export interface SwitcherItem {
  id: string
  name: string
  status: OrgStatus | ProjectStatus
}

interface SwitcherProps {
  /** Names the trigger and the list for assistive tech, for example "Switch org". */
  label: string
  /** What the trigger shows while nothing is selected. */
  placeholder: string
  /** The current item; it may not be in `items` yet when its page is not loaded. */
  current: SwitcherItem | undefined
  /** Everything loaded so far, already sorted. */
  items: readonly SwitcherItem[]
  hasNextPage: boolean
  isFetchingNextPage: boolean
  fetchNextPage: () => void
  onPick: (item: SwitcherItem) => void
  /** The empty footer slot where later rows add "Create" links. */
  footer?: ReactNode
  /** Hide the trigger's text below 640 px (used to keep only the deepest switcher). */
  className?: string
}

/**
 * An org or project switcher: a button that opens a searchable list. Typing filters the loaded
 * items; reaching the end of the list (by scrolling or arrow keys) loads the next page (AC-13).
 */
export function Switcher({
  label,
  placeholder,
  current,
  items,
  hasNextPage,
  isFetchingNextPage,
  fetchNextPage,
  onPick,
  footer,
  className,
}: SwitcherProps) {
  // State, not refs: the popup mounts after this component renders, and the effect must re-run then.
  const [scroller, setScroller] = useState<HTMLDivElement | null>(null)
  const [lastOption, setLastOption] = useState<HTMLDivElement | null>(null)
  const canLoadMore = hasNextPage && !isFetchingNextPage

  // Watch the last option; when it scrolls into view, load the next page. A search that matches
  // nothing has no last option, so paging waits for "Load more to search further" instead.
  useEffect(() => {
    if (lastOption === null || scroller === null || !canLoadMore) return
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) fetchNextPage()
      },
      { root: scroller, rootMargin: '0px 0px 96px 0px' },
    )
    observer.observe(lastOption)
    return () => {
      observer.disconnect()
    }
  }, [lastOption, scroller, canLoadMore, fetchNextPage])

  return (
    <Combobox.Root<SwitcherItem>
      items={items}
      value={current ?? null}
      onValueChange={(item) => {
        if (item !== null) onPick(item)
      }}
      itemToStringLabel={(item) => item.name}
      isItemEqualToValue={(a, b) => a.id === b.id}
    >
      <Combobox.Trigger
        render={
          <Button
            variant="ghost"
            className={className}
            // The name keeps the visible text (WCAG 2.5.3) and says what the control does.
            aria-label={current === undefined ? label : `${label}: ${current.name}`}
          />
        }
      >
        <span className="max-w-40 truncate">{current?.name ?? placeholder}</span>
        <ChevronsUpDown aria-hidden className="text-muted-foreground" />
      </Combobox.Trigger>
      <Combobox.Portal>
        <Combobox.Positioner sideOffset={6} align="start" className="z-50">
          <Combobox.Popup className="w-72 max-w-(--available-width) origin-(--transform-origin) rounded-lg border border-border bg-popover text-popover-foreground shadow-(--shadow-overlay) duration-150 data-closed:animate-out data-closed:fade-out-0 data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95">
            <div className="border-b border-border p-1.5">
              <Combobox.Input
                aria-label={`Search: ${label}`}
                placeholder="Search"
                className="h-(--control-h-sm) w-full rounded-md border border-input bg-background px-2 text-body placeholder:text-muted-foreground"
              />
            </div>
            <div ref={setScroller} className="max-h-72 overflow-y-auto overscroll-contain">
              <Combobox.Empty className="px-3 py-2 text-body text-muted-foreground">
                <span>No match.</span>
                {hasNextPage ? (
                  <Button
                    variant="link"
                    size="sm"
                    className="mt-1 block"
                    loading={isFetchingNextPage}
                    onClick={() => {
                      fetchNextPage()
                    }}
                  >
                    Load more to search further
                  </Button>
                ) : null}
              </Combobox.Empty>
              <SwitcherList label={label} lastOptionRef={setLastOption} />
              {isFetchingNextPage ? (
                <div className="flex items-center gap-2 px-3 py-2 text-small text-muted-foreground">
                  <Loader2 aria-hidden className="size-3.5 animate-spin" />
                  Loading more
                </div>
              ) : null}
            </div>
            {footer === undefined ? null : (
              <div data-slot="switcher-footer" className="border-t border-border p-1">
                {footer}
              </div>
            )}
          </Combobox.Popup>
        </Combobox.Positioner>
      </Combobox.Portal>
    </Combobox.Root>
  )
}

/** The filtered options; the last one gets `lastOptionRef` so the paging observer can watch it. */
function SwitcherList({
  label,
  lastOptionRef,
}: {
  label: string
  lastOptionRef: Ref<HTMLDivElement>
}) {
  const lastIndex = Combobox.useFilteredItems<SwitcherItem>().length - 1
  return (
    <Combobox.List aria-label={label} className="p-1">
      {(item: SwitcherItem, index: number) => (
        <SwitcherRow
          key={item.id}
          item={item}
          ref={index === lastIndex ? lastOptionRef : undefined}
        />
      )}
    </Combobox.List>
  )
}

function SwitcherRow({ item, ref }: { item: SwitcherItem; ref?: Ref<HTMLDivElement> | undefined }) {
  const { label, tone } = statusLabel(item.status)
  const dimmed = item.status === 'deleting'
  return (
    <Combobox.Item
      ref={ref}
      value={item}
      className="flex min-h-(--row-h) cursor-default items-center gap-2 rounded-md px-2 text-body select-none data-highlighted:bg-accent data-highlighted:text-accent-foreground data-[disabled]:opacity-50"
    >
      <span className={dimmed ? 'flex-1 truncate opacity-60' : 'flex-1 truncate'}>{item.name}</span>
      {item.status === 'active' ? null : (
        <Badge variant="status" tone={tone}>
          {label}
        </Badge>
      )}
      <Combobox.ItemIndicator>
        <Check aria-hidden className="size-(--icon)" />
      </Combobox.ItemIndicator>
    </Combobox.Item>
  )
}
