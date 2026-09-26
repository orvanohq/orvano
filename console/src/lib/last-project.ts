const key = 'orvano.lastProject'
const idPattern = /^[a-z0-9]{1,60}$/

/** The project opened last in this browser, or `undefined` when none is stored or the value is not an ID. */
export function getLastProject(): string | undefined {
  try {
    const value = window.localStorage.getItem(key)
    return value !== null && idPattern.test(value) ? value : undefined
  } catch {
    return undefined
  }
}

/** Remembers the project a person opened, so `/` can send them back to it. */
export function setLastProject(projectId: string): void {
  try {
    if (idPattern.test(projectId)) window.localStorage.setItem(key, projectId)
  } catch {
    // Storage can be blocked; `/` then opens the org list.
  }
}

/** Forgets `projectId` when it is the stored one (the project no longer exists for this person). */
export function clearLastProject(projectId: string): void {
  try {
    if (window.localStorage.getItem(key) === projectId) window.localStorage.removeItem(key)
  } catch {
    // Nothing to clear when storage is blocked.
  }
}
