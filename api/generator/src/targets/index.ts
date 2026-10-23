// Spec 0014 GNR-3: every target, what it renders, and what it owns in its repo.
import { GeneratorError } from "../contract.ts";
import { scenariosFor, type ConformanceInputs } from "../conformance.ts";
import { buildModel, forAudience, type Audience, type Model } from "../model.ts";
import { byString } from "../naming.ts";
import type { OutputFile, Ownership, Provenance } from "../output.ts";
import { csharpOwnership, csharpTargets, renderCsharp } from "./csharp/index.ts";
import { dartOwnership, dartTargets, renderDart } from "./dart/index.ts";
import { renderTypescript, typescriptOwnership, typescriptTargets } from "./typescript/index.ts";

export interface Target {
  name: string;
  audience: Audience;
  ownership: Ownership;
  render(model: Model, source: Provenance, conformance: ConformanceInputs): OutputFile[];
}

/** GNR-7: no hand written templates exist yet; one named in the contract must be added before it renders. */
function checkTemplates(view: Model, target: string): void {
  for (const operation of view.services.flatMap((service) => service.operations)) {
    if (operation.template !== undefined) {
      throw new GeneratorError(
        `${operation.operationId}: no ${target} template named ${operation.template}.`,
      );
    }
  }
}

function conformanceFor(model: Model, audience: Audience, conformance: ConformanceInputs) {
  const fixture = buildModel(conformance.fixture);
  return {
    fixture: forAudience(fixture, audience),
    scenarios: scenariosFor(conformance.scenarios, audience, model, fixture),
  };
}

const targets: Target[] = [
  ...Object.entries(typescriptTargets).map(([name, settings]): Target => ({
    name,
    audience: settings.audience,
    ownership: typescriptOwnership,
    render: (model, source, conformance) => {
      const view = forAudience(model, settings.audience);
      checkTemplates(view, name);
      return renderTypescript(
        view,
        source,
        settings.sdkPackage,
        conformanceFor(model, settings.audience, conformance),
      );
    },
  })),
  ...Object.entries(dartTargets).map(([name, settings]): Target => ({
    name,
    audience: settings.audience,
    ownership: dartOwnership,
    render: (model, source, conformance) => {
      const view = forAudience(model, settings.audience);
      checkTemplates(view, name);
      return renderDart(
        view,
        source,
        settings.dartPackage,
        conformanceFor(model, settings.audience, conformance),
      );
    },
  })),
  ...Object.entries(csharpTargets).map(([name, settings]): Target => ({
    name,
    audience: settings.audience,
    ownership: csharpOwnership,
    render: (model, source, conformance) => {
      const view = forAudience(model, settings.audience);
      checkTemplates(view, name);
      return renderCsharp(view, source, conformanceFor(model, settings.audience, conformance));
    },
  })),
].toSorted((a, b) => byString(a.name, b.name));

export const targetNames: readonly string[] = targets.map((target) => target.name);

export function findTarget(name: string): Target {
  const target = targets.find((entry) => entry.name === name);
  if (target === undefined)
    throw new GeneratorError(`Unknown target ${name}. Targets: ${targetNames.join(", ")}.`);
  return target;
}
