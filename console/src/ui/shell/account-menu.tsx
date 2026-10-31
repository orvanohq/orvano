import { useLingui } from "@lingui/react/macro";
import { Button as AriaButton } from "react-aria-components";
import { setDensity, useLayout, type Density } from "@/shared/stores/layout";
import {
  setThemePreference,
  useThemePreference,
  type ThemePreference,
} from "@/shared/stores/theme";
import { Avatar } from "../avatar";
import { cx, focusRing } from "../cx";
import { AccountProfileIcon, ShortcutsIcon, SignOutIcon } from "../icons";
import { Menu, MenuItem, MenuSection, MenuSeparator, MenuTrigger } from "../menu";
import type { ShellModel } from "./types";

export interface AccountMenuProps {
  user: ShellModel["user"];
  onShowAccount?: () => void;
  onSignOut: () => void;
  /** Opens `ShortcutHelp`, which lands with milestone 5 (PRM-10). */
  onShowShortcuts?: () => void;
}

/**
 * Spec 0015 AC-11: the avatar button (named "Account") at the top bar's inline end. The menu shows the name and
 * email, Theme and Density as single selection sections, the account page, Keyboard shortcuts, and Sign out.
 */
export function AccountMenu({ user, onShowAccount, onSignOut, onShowShortcuts }: AccountMenuProps) {
  const { t } = useLingui();
  const theme = useThemePreference();
  const density = useLayout(user.id, (layout) => layout.density);

  return (
    <MenuTrigger
      placement="bottom end"
      trigger={
        <AriaButton
          aria-label={t`Account`}
          className={cx(
            "inline-flex size-control items-center justify-center rounded-full",
            focusRing,
          )}
        >
          <Avatar name={user.name} email={user.email} />
        </AriaButton>
      }
      header={
        <div className="flex items-center gap-3 border-b px-3 py-3">
          <Avatar name={user.name} email={user.email} />
          <div className="flex min-w-0 flex-col">
            <span className="truncate font-medium">{user.name}</span>
            <span className="truncate text-sm text-fg-muted">{user.email}</span>
          </div>
        </div>
      }
    >
      <Menu aria-label={t`Account`}>
        <MenuSection
          title={t`Theme`}
          selectedKey={theme}
          onSelect={(key) => {
            setThemePreference(key as ThemePreference);
          }}
        >
          <MenuItem id="system">{t`System`}</MenuItem>
          <MenuItem id="light">{t`Light`}</MenuItem>
          <MenuItem id="dark">{t`Dark`}</MenuItem>
        </MenuSection>
        <MenuSection
          title={t`Density`}
          selectedKey={density}
          onSelect={(key) => {
            setDensity(user.id, key as Density);
          }}
        >
          <MenuItem id="comfortable">{t`Comfortable`}</MenuItem>
          <MenuItem id="compact">{t`Compact`}</MenuItem>
        </MenuSection>
        <MenuSeparator />
        {onShowAccount ? (
          <MenuItem id="account" icon={AccountProfileIcon} onAction={onShowAccount}>
            {t`Account`}
          </MenuItem>
        ) : null}
        <MenuItem id="shortcuts" icon={ShortcutsIcon} onAction={onShowShortcuts}>
          {t`Keyboard shortcuts`}
        </MenuItem>
        <MenuItem id="sign-out" icon={SignOutIcon} onAction={onSignOut}>
          {t`Sign out`}
        </MenuItem>
      </Menu>
    </MenuTrigger>
  );
}
