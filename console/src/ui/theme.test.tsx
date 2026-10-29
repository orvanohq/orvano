import { CheckboxButton, CheckboxField } from "react-aria-components";
import { afterEach, describe, expect, it } from "vitest";
import { render } from "vitest-browser-react";
import { emulateForcedColors } from "@/test/media";

// Spec 0015 STY-1, AC-2: Tailwind's default scales are reset, so only the semantic tokens exist as utilities.

const transparent = "rgba(0, 0, 0, 0)";

function styleOf(className: string): CSSStyleDeclaration {
  const element = document.querySelector(`[data-probe="${className}"]`);
  if (!element) throw new Error(`No probe for ${className}`);
  return getComputedStyle(element);
}

/** A probe's rendered width and height. */
function size(probe: string): { width?: number; height?: number } {
  const box = document.querySelector(`[data-probe="${probe}"]`)?.getBoundingClientRect();
  return { width: box?.width, height: box?.height };
}

const rowHeight = () =>
  getComputedStyle(document.documentElement).getPropertyValue("--row-height").trim();

describe("theme.css", () => {
  afterEach(() => {
    delete document.documentElement.dataset.theme;
    delete document.documentElement.dataset.density;
  });

  it("0015 AC-2: default palette, font, text, radius, and shadow utilities compile to nothing", async () => {
    const probes = ["bg-red-500", "rounded-xl", "shadow-md", "text-2xl", "font-serif"];
    await render(
      <div>
        {probes.map((probe) => (
          <span key={probe} data-probe={probe} className={probe}>
            {probe}
          </span>
        ))}
        <span data-probe="text-black" className="text-black">
          text-black
        </span>
      </div>,
    );

    expect(styleOf("bg-red-500").backgroundColor).toBe(transparent);
    expect(styleOf("rounded-xl").borderTopLeftRadius).toBe("0px");
    expect(styleOf("shadow-md").boxShadow).toBe("none");
    // text-2xl and font-serif fall back to the body's 14 px system font.
    expect(styleOf("text-2xl").fontSize).toBe("14px");
    expect(styleOf("font-serif").fontFamily).toBe(getComputedStyle(document.body).fontFamily);
    expect(styleOf("text-black").color).toBe(getComputedStyle(document.body).color);
  });

  it("0015 AC-2: the semantic tokens exist as utilities", async () => {
    await render(
      <span data-probe="tokens" className="rounded-md bg-accent text-xl shadow-lg">
        Tokens
      </span>,
    );

    const style = styleOf("tokens");
    expect(style.backgroundColor).not.toBe(transparent);
    expect(style.borderTopLeftRadius).toBe("6px");
    expect(style.fontSize).toBe("24px");
    expect(style.boxShadow).not.toBe("none");
  });

  it("0015 AC-4: data-theme switches the tokens", async () => {
    await render(<span data-probe="surface" className="bg-bg" />);

    document.documentElement.dataset.theme = "light";
    const light = styleOf("surface").backgroundColor;
    document.documentElement.dataset.theme = "dark";
    const dark = styleOf("surface").backgroundColor;

    expect(light).not.toBe(dark);
  });

  it("0015 AC-11: data-density sets the row height", () => {
    expect(rowHeight()).toBe("2.5rem");
    document.documentElement.dataset.density = "compact";
    expect(rowHeight()).toBe("1.75rem");
  });

  it("0015 AC-6: in forced colors the current item gains a mark and a checked checkbox keeps its size (THM-6)", async ({
    skip,
  }) => {
    await render(
      <div>
        <CheckboxField defaultSelected>
          <CheckboxButton data-probe="checkbox" className="inline-flex items-center gap-2">
            <span className="size-4 rounded-sm border border-border-strong" />
            Checked
          </CheckboxButton>
        </CheckboxField>
        <a data-probe="current" href="#current" aria-current="page" className="inline-block px-3">
          Current
        </a>
      </div>,
    );
    const before = { checkbox: size("checkbox"), current: size("current") };

    await emulateForcedColors(true);
    try {
      // WebKit doesn't emulate forced colors (Safari has no such mode).
      if (!window.matchMedia("(forced-colors: active)").matches) skip();

      expect(size("checkbox")).toEqual(before.checkbox);
      expect(size("current")).toEqual(before.current);
      const current = styleOf("current");
      expect(current.outlineStyle).toBe("solid");
      expect(current.outlineWidth).toBe("2px");
      expect(document.querySelector('[data-probe="checkbox"]')?.hasAttribute("data-selected")).toBe(
        true,
      );
      expect(styleOf("checkbox").outlineStyle).toBe("none");
    } finally {
      await emulateForcedColors(false);
    }
  });
});
