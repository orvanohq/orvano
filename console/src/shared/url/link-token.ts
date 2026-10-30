/**
 * Spec 0003 URL-5: a token that arrives in a link's fragment (`#token=…`) is read once, and the fragment is dropped
 * from the address with `history.replaceState` (keeping the router's history state), so the token stays in neither
 * the address bar nor the session history. The fragment never reaches the server, its logs, or a `Referer`. Returns
 * the value, or `undefined` when the fragment has none; any other fragment parameters stay.
 */
export function takeFragmentParam(name: string): string | undefined {
  const fragment = window.location.hash.replace(/^#/, "");
  if (fragment === "") return undefined;
  const params = new URLSearchParams(fragment);
  const value = params.get(name);
  if (value === null) return undefined;
  params.delete(name);
  const rest = params.toString();
  const { pathname, search } = window.location;
  window.history.replaceState(
    window.history.state,
    "",
    `${pathname}${search}${rest ? `#${rest}` : ""}`,
  );
  return value;
}
