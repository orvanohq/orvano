import { useEffect, useRef, useState } from "react";
import { useLingui } from "@lingui/react/macro";
import { useRouter } from "@tanstack/react-router";
import {
  Autocomplete,
  Dialog,
  Input,
  Modal,
  ModalOverlay,
  TextField,
  useFilter,
} from "react-aria-components";
import { paletteRecents, PALETTE_RECENTS_LIMIT } from "@/shared/storage/items";
import { Icon } from "../icon";
import { RecentIcon, SearchIcon } from "../icons";
import { Menu, MenuItem, MenuSection } from "../menu";
import { linkOptions } from "../shell/link-options";
import {
  paletteSectionLabels,
  type PaletteAction,
  type PaletteSection,
} from "../shell/palette-actions";

// Spec 0015 PRM-10: imported by path (`@/ui/command-palette`) and loaded lazily where it first appears.

export interface CommandPaletteProps {
  isOpen: boolean;
  onOpenChange: (isOpen: boolean) => void;
  /** The local actions for the current scope (`paletteActions`, SHL-10). */
  actions: readonly PaletteAction[];
  /** Keys the recents item (`orvano:v1:{userId}:palette`). */
  userId: string;
}

const RECENT_PREFIX = "recent:";
const sections: readonly PaletteSection[] = ["navigation", "theme", "density", "help"];

/**
 * design.md _Shell_, spec 0001 UX-25 (the SHL-10 subset): a modal dialog holding a combobox over a menu of actions
 * (React Aria `Autocomplete` with a `Menu`, PRM-8). An empty query shows your recent actions first. Arrows move through
 * the actions while focus stays in the input, Enter runs one, and Escape closes it; focus returns to where it was.
 */
export function CommandPalette({ isOpen, onOpenChange, actions, userId }: CommandPaletteProps) {
  const { t } = useLingui();
  return (
    <ModalOverlay
      data-modal // SHL-10: Mod+K does nothing while a modal, this one included, is open
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      isDismissable
      className="fixed inset-0 z-(--z-modal) flex items-start justify-center overflow-y-auto bg-scrim p-4 pt-16 transition-opacity duration-(--duration-base) ease-standard data-entering:opacity-0 data-exiting:opacity-0 lg:pt-24"
    >
      <Modal className="w-palette max-w-full rounded-lg border bg-bg-raised text-fg shadow-lg transition duration-(--duration-base) ease-standard data-entering:scale-98 data-exiting:scale-98">
        <Dialog aria-label={t`Command palette`} className="flex flex-col outline-none">
          {({ close }) => <PaletteBody actions={actions} userId={userId} close={close} />}
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}

function PaletteBody({
  actions,
  userId,
  close,
}: {
  actions: readonly PaletteAction[];
  userId: string;
  close: () => void;
}) {
  const { t, i18n } = useLingui();
  const router = useRouter();
  const { contains } = useFilter({ sensitivity: "base" });
  const [query, setQuery] = useState("");
  const [recentIds] = useState(() => paletteRecents.read({ userId }));
  // The chosen action runs once the palette is gone and React Aria has put focus back where it was (it restores in
  // the next frame), so an action that opens another overlay returns focus there too.
  const pending = useRef<PaletteAction | null>(null);

  useEffect(
    () => () => {
      const action = pending.current;
      if (!action) return;
      pending.current = null;
      requestAnimationFrame(() => {
        if (action.run.kind === "navigate") void router.navigate(linkOptions(action.run.link));
        else action.run.call();
      });
    },
    [router],
  );

  const labeled = actions.map((action) => ({ action, label: action.label(i18n) }));
  const matching = query ? labeled.filter(({ label }) => contains(label, query)) : labeled;
  const recents = query
    ? []
    : recentIds.flatMap((id) => labeled.filter(({ action }) => action.id === id));

  const run = (key: string) => {
    const id = key.startsWith(RECENT_PREFIX) ? key.slice(RECENT_PREFIX.length) : key;
    const action = actions.find((candidate) => candidate.id === id);
    if (!action) return;
    paletteRecents.write(
      { userId },
      [id, ...paletteRecents.read({ userId }).filter((recent) => recent !== id)].slice(
        0,
        PALETTE_RECENTS_LIMIT,
      ),
    );
    pending.current = action;
    close();
  };

  return (
    <Autocomplete inputValue={query} onInputChange={setQuery}>
      {/* A TextField, not a SearchField: Escape closes the palette at once instead of first clearing the query. */}
      <TextField
        aria-label={t`Search actions`}
        // oxlint-disable-next-line jsx-a11y/no-autofocus -- APG dialog with a combobox: opening the palette puts focus in its input
        autoFocus
        className="flex items-center gap-2 border-b px-3"
      >
        <Icon icon={SearchIcon} />
        <Input
          // AC-13, the APG combobox: React Aria's Autocomplete wires aria-controls and aria-activedescendant to the
          // menu but leaves the input a plain textbox, so it is named a combobox whose popup is always shown.
          // oxlint-disable-next-line jsx-a11y/prefer-tag-over-role, jsx-a11y/role-has-required-aria-props -- an input already; Autocomplete adds aria-controls at runtime
          role="combobox"
          aria-expanded
          placeholder={t`Search actions`}
          className="h-control-lg min-w-0 flex-1 bg-transparent text-fg outline-none"
        />
      </TextField>
      <div className="max-h-96">
        <Menu
          aria-label={t`Actions`}
          emptyMessage={t`No matching actions`}
          onAction={(key) => {
            run(String(key));
          }}
        >
          {recents.length > 0 && (
            <MenuSection title={t`Recent`}>
              {recents.map(({ action, label }) => (
                <MenuItem key={action.id} id={`${RECENT_PREFIX}${action.id}`} icon={RecentIcon}>
                  {label}
                </MenuItem>
              ))}
            </MenuSection>
          )}
          {sections.map((section) => {
            const items = matching.filter(({ action }) => action.section === section);
            if (items.length === 0) return null;
            return (
              <MenuSection key={section} title={i18n._(paletteSectionLabels[section])}>
                {items.map(({ action, label }) => (
                  <MenuItem key={action.id} id={action.id} icon={action.icon}>
                    {label}
                  </MenuItem>
                ))}
              </MenuSection>
            );
          })}
        </Menu>
      </div>
    </Autocomplete>
  );
}
