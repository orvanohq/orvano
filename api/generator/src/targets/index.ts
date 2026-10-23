// Spec 0014 GNR-3: every target, what it renders, and what it owns in its repo.
import { GeneratorError } from "../contract.ts";
import { forAudience, type Audience, type Model } from "../model.ts";
import type { OutputFile, Ownership, Provenance } from "../output.ts";
import { renderTypescript, typescriptOwnership, typescriptTargets } from "./typescript/index.ts";

export interface Target {
  name: string;
  audience: Audience;
  ownership: Ownership;
  render(model: Model, source: Provenance): OutputFile[];
}

const targets: Target[] = Object.entries(typescriptTargets).map(([name, settings]) => ({
  name,
  audience: settings.audience,
  ownership: typescriptOwnership,
  render: (model, source) => {
    const view = forAudience(model, settings.audience);
    for (const operation of view.services.flatMap((service) => service.operations)) {
      // GNR-7: no hand written templates exist yet; one named in the contract must be added before it renders.
      if (operation.template !== undefined) {
        throw new GeneratorError(
          `${operation.operationId}: no ${name} template named ${operation.template}.`,
        );
      }
    }
    return renderTypescript(view, source, settings.sdkPackage);
  },
}));

export const targetNames: readonly string[] = targets.map((target) => target.name);

export function findTarget(name: string): Target {
  const target = targets.find((entry) => entry.name === name);
  if (target === undefined)
    throw new GeneratorError(`Unknown target ${name}. Targets: ${targetNames.join(", ")}.`);
  return target;
}
