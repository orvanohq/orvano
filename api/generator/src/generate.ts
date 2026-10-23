// Spec 0014 GNR-2, GNR-8: contract and conformance inputs in, files out, the same bytes on every run.
import type { ConformanceInputs } from "./conformance.ts";
import type { LoadedContract } from "./contract.ts";
import { buildModel } from "./model.ts";
import { provenance, type OutputFile } from "./output.ts";
import { findTarget } from "./targets/index.ts";

export function generate(
  contract: LoadedContract,
  conformance: ConformanceInputs,
  targetName: string,
  commit: string,
): OutputFile[] {
  const target = findTarget(targetName);
  const model = buildModel(contract);
  return target.render(model, provenance(model, commit), conformance);
}
