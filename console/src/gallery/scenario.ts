import { useEffect, useState } from "react";
import { SLOW_MS } from "./fixtures";
import { useSettings } from "./settings";

/**
 * GAL-2: fixture data as the toolbar's scenario serves it: `undefined` (loading) for a while when slow, `empty` when
 * empty, and the data otherwise. A fail scenario reaches the switchers through their search instead.
 */
export function useScenarioData<Data>(data: Data, empty: Data): Data | undefined {
  const { scenario } = useSettings();
  const [loadedFor, setLoadedFor] = useState<string | null>(null);

  useEffect(() => {
    if (scenario !== "slow") return;
    const timer = setTimeout(() => {
      setLoadedFor(scenario);
    }, SLOW_MS);
    return () => {
      clearTimeout(timer);
      setLoadedFor(null);
    };
  }, [scenario]);

  if (scenario === "empty") return empty;
  if (scenario === "slow" && loadedFor !== "slow") return undefined;
  return data;
}
