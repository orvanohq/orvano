import { describe, expect, it } from "vitest";
import themeCss from "./theme.css?raw";

// Spec 0015 TKN-7: every text, paired foreground, boundary, and focus token meets its contrast minimum on every
// surface, in both themes, computed from theme.css itself.

/** The custom properties declared directly in the first block whose selector is `selector`. */
function block(selector: string): Map<string, string> {
  const css = themeCss.replaceAll(/\/\*[\s\S]*?\*\//g, "");
  const start = css.indexOf(`${selector} {`);
  if (start === -1) throw new Error(`theme.css has no ${selector} block`);
  const body = css.slice(css.indexOf("{", start) + 1, css.indexOf("}", start));
  const tokens = new Map<string, string>();
  for (const match of body.matchAll(/--color-([\w-]+)\s*:\s*([^;]+);/g)) {
    if (match[1] && match[2]) tokens.set(match[1], match[2].replaceAll(/\s+/g, " ").trim());
  }
  return tokens;
}

const clip = (channel: number) => Math.min(1, Math.max(0, channel));

/** OKLCH to linear sRGB, each channel clipped to 0 to 1 (colors outside sRGB). */
function toLinearSrgb(value: string): [number, number, number] {
  const match = /^oklch\(\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*(?:\/\s*[\d.]+\s*)?\)$/.exec(value);
  if (!match) throw new Error(`Not an OKLCH color: ${value}`);
  const [lightness, chroma, hue] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const radians = (hue * Math.PI) / 180;
  const a = chroma * Math.cos(radians);
  const b = chroma * Math.sin(radians);
  const l = (lightness + 0.3963377774 * a + 0.2158037573 * b) ** 3;
  const m = (lightness - 0.1055613458 * a - 0.0638541728 * b) ** 3;
  const s = (lightness - 0.0894841775 * a - 1.291485548 * b) ** 3;
  return [
    clip(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
    clip(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
    clip(-0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s),
  ];
}

function luminance(value: string): number {
  const [red, green, blue] = toLinearSrgb(value);
  return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
}

function contrast(first: string, second: string): number {
  const [light, dark] = [luminance(first), luminance(second)].toSorted((x, y) => y - x) as [
    number,
    number,
  ];
  return (light + 0.05) / (dark + 0.05);
}

const light = block("@theme");
const themes = {
  light,
  dark: new Map([...light, ...block('[data-theme="dark"]')]),
};

const surfaces = [
  "bg",
  "bg-subtle",
  "bg-muted",
  "bg-raised",
  "accent-subtle",
  "danger-subtle",
  "success-subtle",
  "warning-subtle",
  "info-subtle",
];
const statuses = ["danger", "success", "warning", "info"];
const texts = ["fg", "fg-muted", "accent-text", ...statuses.map((status) => `${status}-text`)];

/** [foreground, background, minimum] for every pair TKN-7 names. */
const pairs: [string, string, number][] = [
  ...texts.flatMap((text) =>
    surfaces
      // A status text on another status's surface never appears.
      .filter((surface) => {
        const textStatus = statuses.find((status) => text.startsWith(status));
        const surfaceStatus = statuses.find((status) => surface.startsWith(status));
        return !textStatus || !surfaceStatus || textStatus === surfaceStatus;
      })
      .map((surface): [string, string, number] => [text, surface, 4.5]),
  ),
  ["accent-fg", "accent", 4.5],
  ["accent-fg", "accent-hover", 4.5],
  ["danger-fg", "danger", 4.5],
  ["danger-fg", "danger-hover", 4.5],
  ["accent-subtle-fg", "accent-subtle", 4.5],
  ["tooltip-fg", "tooltip", 4.5],
  ...["border-strong", "focus", "accent"].flatMap((boundary) =>
    surfaces.map((surface): [string, string, number] => [boundary, surface, 3]),
  ),
];

describe("theme.css contrast (TKN-7)", () => {
  for (const [name, tokens] of Object.entries(themes)) {
    it.each(pairs)(
      `0015 AC-5: ${name}: %s on %s is at least %d:1`,
      (foreground, background, minimum) => {
        const front = tokens.get(foreground);
        const back = tokens.get(background);
        expect(front, `--color-${foreground}`).toBeDefined();
        expect(back, `--color-${background}`).toBeDefined();
        expect(contrast(front ?? "", back ?? "")).toBeGreaterThanOrEqual(minimum);
      },
    );
  }

  it("0015 AC-5: the dark tokens are the same with data-theme and with the OS media query (THM-3)", () => {
    expect(block(":root:not([data-theme])")).toEqual(block('[data-theme="dark"]'));
  });

  it("0015 AC-5: a low contrast token fails the check", () => {
    expect(contrast("oklch(0.8 0 0)", "oklch(1 0 0)")).toBeLessThan(4.5);
    expect(contrast("oklch(0 0 0)", "oklch(1 0 0)")).toBeCloseTo(21, 0);
  });
});
