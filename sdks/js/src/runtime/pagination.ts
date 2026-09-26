/** One page of a cursor list operation: `{ items, nextCursor }`. */
export interface Page<T> {
  /** The items on this page. */
  items: T[]
  /** Pass it as `cursor` to get the next page; null on the last page. */
  nextCursor: string | null
}

/**
 * Walks every page of a list operation, yielding each item. Generated `...All` methods use it;
 * you can too: `for await (const item of paginate((cursor) => orvano.x.list({ cursor })))`.
 */
export async function* paginate<T>(
  fetchPage: (cursor: string | undefined) => Promise<Page<T>>,
): AsyncGenerator<T> {
  let cursor: string | undefined
  do {
    const page = await fetchPage(cursor)
    yield* page.items
    cursor = page.nextCursor ?? undefined
  } while (cursor !== undefined)
}
