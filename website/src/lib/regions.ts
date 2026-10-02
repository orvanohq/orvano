const marker = /^\s*(?:\/\/|#|<!--|\{\/\*)\s*#(end)?region\b\s*([\w-]*)/

/**
 * The lines of `source` between `#region <name>` and `#endregion <name>` (in a `//`, `#`, `<!--`, or
 * `{/*` comment), with their common indent removed and every other region marker dropped. Without a
 * name, the whole file without markers. Throws when the region is missing, so the build fails (AC-18).
 */
export function extractRegion(source: string, name: string | undefined, where: string): string {
  const lines = source.replace(/\r\n/g, '\n').split('\n')
  let picked = lines
  if (name !== undefined) {
    const start = lines.findIndex(
      (line) => marker.exec(line)?.[1] === undefined && marker.exec(line)?.[2] === name,
    )
    const end = lines.findIndex(
      (line, i) => i > start && marker.exec(line)?.[1] === 'end' && marker.exec(line)?.[2] === name,
    )
    if (start < 0 || end < 0) throw new Error(`The code region "${name}" is missing from ${where}.`)
    picked = lines.slice(start + 1, end)
  }

  const kept = picked.filter((line) => !marker.test(line))
  while (kept.length > 0 && kept.at(-1)?.trim() === '') kept.pop()
  while (kept.length > 0 && kept[0]?.trim() === '') kept.shift()
  const indent = Math.min(
    ...kept.filter((line) => line.trim() !== '').map((line) => /^ */.exec(line)?.[0].length ?? 0),
  )
  return kept.map((line) => line.slice(Number.isFinite(indent) ? indent : 0)).join('\n')
}
