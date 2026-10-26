import { useSyncExternalStore } from "react";

// Spec 0015 SHL-6: the one breakpoint check, the same as Tailwind's `lg`, so CSS and JavaScript agree at any default
// font size. Below it the sidebars are drawers.
function media(): MediaQueryList {
  return window.matchMedia("(min-width: 64rem)");
}

function subscribe(onChange: () => void): () => void {
  const list = media();
  list.addEventListener("change", onChange);
  return () => {
    list.removeEventListener("change", onChange);
  };
}

export function useIsWide(): boolean {
  return useSyncExternalStore(subscribe, () => media().matches);
}
