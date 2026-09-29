import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { LogOut, Server, UserRound } from 'lucide-react'

import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { consoleApi } from '@/lib/console-client'
import { usePreferences, type Density, type ThemeChoice } from '@/lib/preferences'
import { accountQuery } from '@/lib/queries'
import { notifyError } from '@/lib/toast'

/**
 * The account menu: who is signed in, theme and density, Install settings for install admins
 * (spec 0008, AC-24), and Sign out (spec 0004, AC-27).
 */
export function AccountMenu() {
  const { theme, density, setTheme, setDensity } = usePreferences()
  const account = useQuery(accountQuery())
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const signOut = async () => {
    try {
      await consoleApi().consoleAccount.deleteSession()
    } catch (error) {
      notifyError("Couldn't sign out", error)
      return
    }
    // Leave the shell before clearing: a query still on screen would fetch again, get a 401, and
    // send you to /sign-in?redirect=/sign-in.
    await navigate({ to: '/sign-in', replace: true })
    queryClient.clear()
  }
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={<Button variant="ghost" size="icon" aria-label="Account menu" />}
      >
        <UserRound aria-hidden />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        {account.data === undefined ? null : (
          <>
            <DropdownMenuGroup>
              <DropdownMenuLabel className="flex flex-col gap-0.5">
                <span className="truncate font-medium text-foreground">
                  {account.data.name ?? account.data.email}
                </span>
                {account.data.name === null ? null : (
                  <span className="truncate text-muted-foreground">{account.data.email}</span>
                )}
              </DropdownMenuLabel>
            </DropdownMenuGroup>
            <DropdownMenuSeparator />
          </>
        )}
        <DropdownMenuGroup>
          <DropdownMenuLabel>Theme</DropdownMenuLabel>
          <DropdownMenuRadioGroup
            value={theme}
            onValueChange={(value) => {
              setTheme(value as ThemeChoice)
            }}
          >
            <DropdownMenuRadioItem value="light">Light</DropdownMenuRadioItem>
            <DropdownMenuRadioItem value="dark">Dark</DropdownMenuRadioItem>
            <DropdownMenuRadioItem value="system">System</DropdownMenuRadioItem>
          </DropdownMenuRadioGroup>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuGroup>
          <DropdownMenuLabel>Density</DropdownMenuLabel>
          <DropdownMenuRadioGroup
            value={density}
            onValueChange={(value) => {
              setDensity(value as Density)
            }}
          >
            <DropdownMenuRadioItem value="compact">Compact</DropdownMenuRadioItem>
            <DropdownMenuRadioItem value="comfortable">Comfortable</DropdownMenuRadioItem>
          </DropdownMenuRadioGroup>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        {/* Install admins only (spec 0008, AC-24). */}
        {account.data?.isInstallAdmin === true ? (
          <DropdownMenuItem
            onClick={() => {
              void navigate({ to: '/install' })
            }}
          >
            <Server aria-hidden />
            Install settings
          </DropdownMenuItem>
        ) : null}
        <DropdownMenuItem
          onClick={() => {
            void signOut()
          }}
        >
          <LogOut aria-hidden />
          Sign out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
