import { Link } from '@tanstack/react-router'
import type { ReactNode } from 'react'

import { AccountMenu } from '@/shell/account-menu'
import { Logo } from '@/shell/logo'

/** The top bar: logo, the switchers passed as `children`, and the account menu. */
export function TopBar({ leading, children }: { leading?: ReactNode; children?: ReactNode }) {
  return (
    <header className="sticky top-0 z-(--z-sticky) flex h-(--topbar-h) items-center gap-2 border-b border-border bg-background px-3">
      {leading}
      <Link to="/orgs" className="rounded-md px-1 py-1" aria-label="Orvano, all orgs">
        <Logo />
      </Link>
      <div className="mx-1 flex min-w-0 flex-1 items-center gap-1">{children}</div>
      <AccountMenu />
    </header>
  )
}
