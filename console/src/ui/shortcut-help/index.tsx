import { useLingui } from "@lingui/react/macro";
import { useShortcuts } from "@/shared/stores/shortcuts";
import { Button } from "../button";
import { Dialog } from "../dialog";
import { Kbd } from "../kbd";

// Spec 0015 PRM-10: imported by path (`@/ui/shortcut-help`) and loaded lazily where it first appears.

export interface ShortcutHelpProps {
  isOpen: boolean;
  onOpenChange: (isOpen: boolean) => void;
}

/**
 * Spec 0003 A11Y-7, spec 0001 UX-26: the `?` list. It reads the `useShortcut` registry, so it shows exactly what is
 * bound, each with its keys for this platform. Escape closes it and focus returns to where it was.
 */
export function ShortcutHelp({ isOpen, onOpenChange }: ShortcutHelpProps) {
  const { t, i18n } = useLingui();
  const shortcuts = useShortcuts();
  return (
    <Dialog
      title={t`Keyboard shortcuts`}
      size="md"
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      footer={
        <Button slot="close" variant="primary">
          {t`Close`}
        </Button>
      }
    >
      <dl className="flex flex-col divide-y">
        {shortcuts.map((shortcut) => (
          <div key={shortcut.id} className="flex items-center justify-between gap-4 py-2">
            <dt>{i18n._(shortcut.label)}</dt>
            <dd>
              <Kbd keys={shortcut.keys} />
            </dd>
          </div>
        ))}
      </dl>
    </Dialog>
  );
}
