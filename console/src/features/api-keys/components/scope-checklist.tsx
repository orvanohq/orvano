import { msg } from "@lingui/core/macro";
import type { MessageDescriptor } from "@lingui/core";
import { useLingui } from "@lingui/react/macro";
import { CheckboxControl } from "@/ui/form/choice-fields";
import { PUBLISHABLE_SCOPES, SECRET_SCOPES, withScope, type Scope } from "../schemas/create-key";

/** Spec 0017 AKY-4: the checklist's labels, keyed by scope name. */
const scopeLabels: Record<Scope, MessageDescriptor> = {
  "databases.read": msg`Read databases`,
  "databases.write": msg`Write databases`,
  "functions.execute": msg`Run functions`,
  "functions.read": msg`Read functions`,
  "functions.write": msg`Write functions`,
  "messaging.read": msg`Read messaging`,
  "messaging.write": msg`Send messages`,
  "realtime.subscribe": msg`Subscribe to realtime`,
  "sites.read": msg`Read sites`,
  "sites.write": msg`Write sites`,
  "storage.read": msg`Read storage`,
  "storage.write": msg`Write storage`,
  "users.read": msg`Read users`,
  "users.write": msg`Write users`,
  "client.auth": msg`Sign in`,
  "client.auth.signup": msg`Sign up`,
  "client.databases": msg`Databases`,
  "client.functions": msg`Functions`,
  "client.realtime": msg`Realtime`,
  "client.storage": msg`Storage`,
};

const serviceLabels: Record<string, MessageDescriptor> = {
  client: msg`Client APIs`,
  databases: msg`Databases`,
  functions: msg`Functions`,
  messaging: msg`Messaging`,
  realtime: msg`Realtime`,
  sites: msg`Sites`,
  storage: msg`Storage`,
  users: msg`Users`,
};

/** The text before a scope's first dot is its service. */
function byService(scopes: readonly Scope[]): [string, Scope[]][] {
  const services = new Map<string, Scope[]>();
  for (const scope of scopes) {
    const service = scope.slice(0, scope.indexOf("."));
    services.set(service, [...(services.get(service) ?? []), scope]);
  }
  return [...services];
}

interface ScopeChecklistProps {
  type: "publishable" | "secret";
  value: readonly string[];
  onChange: (scopes: string[]) => void;
  onBlur: () => void;
}

/** Spec 0017 AKY-4: scopes grouped by service, each service with "select all". */
export function ScopeChecklist({ type, value, onChange, onBlur }: ScopeChecklistProps) {
  const { t, i18n } = useLingui();
  const services = byService(type === "secret" ? SECRET_SCOPES : PUBLISHABLE_SCOPES);
  return (
    <div className="flex flex-col gap-3">
      {services.map(([service, scopes]) => {
        const name = i18n._(serviceLabels[service] ?? msg`Other`);
        const ticked = scopes.filter((scope) => value.includes(scope));
        return (
          <fieldset key={service} className="flex flex-col">
            <legend className="sr-only">{name}</legend>
            <CheckboxControl
              label={t`All of ${name}`}
              onBlur={onBlur}
              isSelected={ticked.length === scopes.length}
              isIndeterminate={ticked.length > 0 && ticked.length < scopes.length}
              onChange={(isOn) => {
                const others = value.filter((scope) => !(scopes as string[]).includes(scope));
                onChange(isOn ? [...others, ...scopes] : others);
              }}
            />
            <div className="flex flex-col ps-6">
              {scopes.map((scope) => (
                <CheckboxControl
                  key={scope}
                  label={i18n._(scopeLabels[scope])}
                  isSelected={value.includes(scope)}
                  isDisabled={scope === "client.auth.signup" && !value.includes("client.auth")}
                  onBlur={onBlur}
                  onChange={(isOn) => {
                    onChange(withScope(value, scope, isOn));
                  }}
                />
              ))}
            </div>
          </fieldset>
        );
      })}
    </div>
  );
}
