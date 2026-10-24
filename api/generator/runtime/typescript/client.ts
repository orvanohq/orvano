import { sdkPackage } from "../generated/contract.js";

/**
 * What one client handle holds. It lives in a private WeakMap, never on the handle, so a credential never shows
 * in a log or a serialized object (spec 0005 KEY-3, SDK-6).
 * @internal
 */
export interface ClientConfig {
  /** The server URL, validated, with no trailing slash (RTM-3, RTM-6). */
  readonly url: string;
  readonly project: string | undefined;
  /** The `Orvano-Client` value, `<package>/<version>` (HDR-3). */
  readonly clientName: string;
  /** Credential headers, such as `Orvano-Key`, sent on every request. */
  readonly credentials: Readonly<Record<string, string>>;
  readonly headers: Readonly<Record<string, string>>;
  readonly fetch: (request: Request) => Promise<Response>;
  /** The per attempt timeout in milliseconds. */
  readonly timeout: number;
  readonly maxRetries: number;
}

/**
 * A configured Orvano client, made by `createClient`. Pass it to a service's constructor. It does no I/O when
 * created and holds no credential you can read.
 * @public
 */
export interface OrvanoClient {
  /** The package that made this client; a service only accepts a client from its own package. */
  readonly sdk: typeof sdkPackage;
  /** The server URL. */
  readonly url: string;
  /** The project the client calls, when it has one. */
  readonly project: string | undefined;
}

/**
 * Options every service method takes as its last argument.
 * @public
 */
export interface RequestOptions {
  /** Cancels the call. A cancelled call rejects with the signal's reason, not an `OrvanoError`. */
  readonly signal?: AbortSignal | undefined;
  /** The per attempt timeout in milliseconds for this call. */
  readonly timeout?: number | undefined;
  /** Extra headers for this call. Credential, idempotency, and SDK identity headers can't be set here. */
  readonly headers?: Readonly<Record<string, string>> | undefined;
  /** Overrides the client's retry setting for this call. */
  readonly retry?: { readonly maxRetries: number } | undefined;
}

/**
 * Options of a write to a versioned resource (spec 0014 CNC-4), on operations that take `If-Match`.
 * @public
 */
export interface IfMatchOptions extends RequestOptions {
  /**
   * The resource's `version` as you read it, sent as `If-Match`. The write answers `412 version_conflict` (an
   * `OrvanoError` with `kind` `conflict`) when the resource changed since. Leave it out to write anyway.
   */
  readonly ifMatch?: string | undefined;
}

/**
 * One page of a list (spec 0014 PAG-2). Pass `nextCursor` as `cursor` for the next page, or use the method's
 * `All` sibling to walk every page.
 * @public
 */
export interface Page<T> {
  /** The items on this page, in the list's order. */
  readonly items: T[];
  /** The cursor for the next page, or null on the last page. */
  readonly nextCursor: string | null;
  /** How many items the whole list has, or null when the operation does not count them. */
  readonly total: number | null;
}

// The one module level state (SDK-6): each handle maps to its own configuration and nothing else.
const configs = new WeakMap<OrvanoClient, ClientConfig>();

/**
 * Makes a client handle for a configuration. `createClient` calls this once.
 * @internal
 */
export function registerClient(config: ClientConfig): OrvanoClient {
  const client: OrvanoClient = Object.freeze({
    sdk: sdkPackage,
    url: config.url,
    project: config.project,
  });
  configs.set(client, config);
  return client;
}

/**
 * Throws a `TypeError` naming the import to use when a service gets a client this package did not make (SHP-2).
 * @internal
 */
export function assertClient(client: OrvanoClient, service: string): void {
  if (!configs.has(client)) {
    throw new TypeError(
      `${service} needs a client made by createClient from ${sdkPackage}. Import ${service} and createClient from ${sdkPackage}.`,
    );
  }
}

/** @internal */
export function configOf(client: OrvanoClient): ClientConfig {
  const config = configs.get(client);
  if (config === undefined)
    throw new TypeError(`This client was not made by createClient from ${sdkPackage}.`);
  return config;
}

/**
 * The project ID for a data plane path (spec 0014 PTH-5).
 * @internal
 */
export function projectId(client: OrvanoClient): string {
  const project = configOf(client).project;
  if (project === undefined || project === "") {
    throw new TypeError("This call needs a project. Pass project to createClient.");
  }
  return project;
}

/**
 * A path parameter's value, refused when empty before any request (spec 0005 ERR-T5).
 * @internal
 */
export function pathParam(name: string, value: string | number): string {
  const text = String(value);
  if (text === "") throw new TypeError(`${name} must not be empty.`);
  return text;
}
