/**
 * Puts active items first, keeping the API's order inside each group (a stable sort). Apply it to
 * the whole loaded list after every page fetch (spec 0005, AC-13).
 */
export function sortActiveFirst<T extends { status: string }>(items: readonly T[]): T[] {
  const rank = (item: T) => (item.status === 'deleting' ? 1 : 0)
  return items
    .map((item, index) => ({ item, index }))
    .sort((a, b) => rank(a.item) - rank(b.item) || a.index - b.index)
    .map(({ item }) => item)
}
