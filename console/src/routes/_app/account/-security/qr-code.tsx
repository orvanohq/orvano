import { useMemo } from 'react'
import { encode } from 'uqr'

/**
 * A QR code drawn in the browser as SVG (spec 0013, AC-42), so the TOTP secret never leaves the
 * page: `uqr` encodes it, and each dark module becomes a square of one path. It keeps dark on light
 * in both themes (`--qr-dark`, `--qr-light`) with a 4 module quiet zone, which every scanner reads.
 */
export function QrCode({ value, label }: { value: string; label: string }) {
  const { path, size } = useMemo(() => {
    const { data } = encode(value, { ecc: 'M', border: 4 })
    let d = ''
    data.forEach((row, y) => {
      row.forEach((dark, x) => {
        if (dark) d += `M${String(x)} ${String(y)}h1v1h-1z`
      })
    })
    return { path: d, size: data.length }
  }, [value])
  return (
    <svg
      role="img"
      aria-label={label}
      viewBox={`0 0 ${String(size)} ${String(size)}`}
      shapeRendering="crispEdges"
      className="size-48 shrink-0 rounded-md"
    >
      <rect width={size} height={size} className="fill-(--qr-light)" />
      <path d={path} className="fill-(--qr-dark)" />
    </svg>
  )
}
