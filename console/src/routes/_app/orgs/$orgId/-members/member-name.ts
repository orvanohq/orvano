import type { ConsoleUserRef, Member } from '@orvano/console-client'

/** What a member goes by: their name, or the part of their email before `@` (spec 0008, AC-15). */
export function memberName(member: Pick<Member, 'name' | 'email'>): string {
  return member.name ?? member.email.split('@', 1)[0]
}

/** Who made something: their name, else their email, else "Deleted account" (spec 0008, AC-18, AC-25). */
export function userRefName(user: ConsoleUserRef | null): string {
  if (user === null) return 'Deleted account'
  return user.name ?? user.email
}
