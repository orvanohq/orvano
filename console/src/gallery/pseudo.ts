import { useSettings } from "./settings";

// Spec 0015 GAL-2, GAL-3: scene strings never enter the catalog, so the gallery runs them through the same transform
// the pseudo catalog gets (lingui.config.ts `pseudoLocale`, which uses the `pseudolocale` package). pseudo.test.ts
// compares the two, so a change to one fails until the other follows.

const options = { prepend: "[", append: "]", extend: 0.35, extendCharacter: "~" };

/** The `pseudolocale` package's accented letters for a to z, then A to Z (MIT), one UTF-16 unit each. */
const plain = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
const accented = "àƀćďēƒĝĥĩĴķĺḿńōƥǫŕśţũvŵxŷźÀßĆĎĒƑĜĤĨĵĶĹḾŃŌƤǪŔŚŢŨVŴXŶŹ";

/** One literal: accented, padded by `extend` on both sides, then bracketed (as `pseudolocale` does it). */
function transform(text: string): string {
  const accentedText = text.replaceAll(/[a-z]/gi, (letter) =>
    accented.charAt(plain.indexOf(letter)),
  );
  const side = options.extendCharacter.repeat(Math.floor((text.length * options.extend) / 2));
  return `${options.prepend}${side}${accentedText}${side}${options.append}`;
}

/**
 * The pseudo form of a scene string. Like Lingui, it transforms each literal between `{placeholders}` and keeps the
 * placeholders as they are.
 */
export function pseudoize(text: string): string {
  return text
    .split(/(\{[^{}]*\})/)
    .map((part) => (part === "" || part.startsWith("{") ? part : transform(part)))
    .join("");
}

/** Returns `g`, which gives a scene string in the gallery's locale: as written, or pseudo localized. */
export function useGalleryText(): (text: string) => string {
  const { locale } = useSettings();
  return locale === "pseudo-LOCALE" ? pseudoize : (text) => text;
}
