import { Combobox } from '@base-ui/react/combobox'
import { Check, ChevronsUpDown, Loader2 } from 'lucide-react'
import { useEffect, useRef, type ReactNode } from 'react'

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
  const scrollRef = useRef<HTMLDivElement | null>(null)
  const sentinelRef = useRef<HTMLDivElement | null>(null)
  const canLoadMore = hasNextPage && !isFetchingNextPage

  // Watch the last row of the list; when it scrolls into view, load the next page.
  useEffect(() => {
    const sentinel = sentinelRef.current
    const root = scrollRef.current
    if (sentinel === null || root === null || !canLoadMore) return
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) fetchNextPage()
      },
      { root },
    )
    observer.observe(sentinel)
    return () => {
      observer.disconnect()
    }
  })

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
            <div ref={scrollRef} className="max-h-72 overflow-y-auto overscroll-contain">
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
              <Combobox.List aria-label={label} className="p-1">
                {(item: SwitcherItem) => <SwitcherRow key={item.id} item={item} />}
              </Combobox.List>
              {isFetchingNextPage ? (
                <div className="flex items-center gap-2 px-3 py-2 text-small text-muted-foreground">
                  <Loader2 aria-hidden className="size-3.5 animate-spin" />
                  Loading more
                </div>
              ) : null}
              <div ref={sentinelRef} aria-hidden className="h-px" />
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

function SwitcherRow({ item }: { item: SwitcherItem }) {
  const { label, tone } = statusLabel(item.status)
  const dimmed = item.status === 'deleting'
  return (
    <Combobox.Item
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
