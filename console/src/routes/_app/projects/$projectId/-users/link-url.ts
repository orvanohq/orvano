/** Where the console remembers the last "Link opens at" URL of a project (spec 0010, AC-23). */
export function linkUrlKey(projectId: string): string {
  return `orvano.console.auth-link-url.${projectId}`
}

/** The URL last used to send a link for `projectId`, or an empty string when none is stored or storage is blocked. */
export function getRememberedLinkUrl(projectId: string): string {
  try {
    return window.localStorage.getItem(linkUrlKey(projectId)) ?? ''
  } catch {
    return ''
  }
}

/** Remembers `url` for the next send in `projectId`. Blocked storage just forgets it. */
export function rememberLinkUrl(projectId: string, url: string): void {
  try {
    window.localStorage.setItem(linkUrlKey(projectId), url)
  } catch {
    // Storage can be blocked; the field then starts empty next time.
  }
}

/**
 * Suggested URLs from the project's web platform hosts: `https://<host>/`, or `http://` for
 * `localhost` and `127.0.0.1`. Wildcard patterns name no single host, so they are left out.
 */
export function suggestedLinkUrls(webHosts: readonly string[]): string[] {
  return webHosts
    .filter((host) => !host.startsWith('*.'))
    .map((host) =>
      host === 'localhost' || host === '127.0.0.1' ? `http://${host}/` : `https://${host}/`,
    )
}
