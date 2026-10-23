// Dart types for the contract's schemas (spec 0004 MDL-1 to MDL-4, spec 0014 GNR-6). Dart has no inline enums or
// object types, so an enum or object written inline in a property gets a class named after its owner and the
// property (`WidgetResponse` + `color` gives `WidgetResponseColor`).
import { GeneratorError } from "../../contract.ts";
import type { NamedSchema, Property, TargetModel, TypeRef } from "../../model.ts";
import { byString, pascalCase } from "../../naming.ts";

export type DartType =
  | { kind: "string" }
  | { kind: "int" }
  | { kind: "double" }
  | { kind: "bool" }
  | { kind: "dateTime" }
  | { kind: "any" }
  | { kind: "enum"; name: string }
  | { kind: "class"; name: string }
  | { kind: "list"; items: DartType }
  | { kind: "map"; values: DartType }
  | { kind: "nullable"; inner: DartType };

/** A Dart enum or class the target emits. */
export type DartDeclaration =
  | { kind: "enum"; name: string; description: string; values: string[] }
  | {
      kind: "class";
      name: string;
      description: string;
      properties: Property[];
      /** Read from responses: keeps unknown enum values so toJson gives them back. */
      output: boolean;
    };

/** Resolves contract types to Dart types and collects every declaration the target emits. */
export class DartTypes {
  readonly #model: TargetModel;
  readonly #schemas: Map<string, NamedSchema>;
  readonly #declarations = new Map<string, { declaration: DartDeclaration; shape: string }>();

  constructor(model: TargetModel) {
    this.#model = model;
    this.#schemas = new Map(model.schemas.map((schema) => [schema.name, schema]));
    for (const schema of model.schemas) {
      const use = model.schemaUse.get(schema.name);
      const where = `Schema ${schema.name}`;
      if (schema.type.kind === "enum") {
        this.#declare(
          {
            kind: "enum",
            name: schema.name,
            description: schema.description,
            values: schema.type.values,
          },
          where,
        );
      } else if (schema.type.kind === "object") {
        this.#declareClass(
          schema.name,
          schema.description,
          schema.type.properties,
          use?.output === true,
          where,
        );
      }
    }
  }

  /** Every declaration, sorted by name. */
  declarations(): DartDeclaration[] {
    return [...this.#declarations.values()]
      .map((entry) => entry.declaration)
      .toSorted((a, b) => byString(a.name, b.name));
  }

  /**
   * The Dart type of a contract type. `owner` names an inline enum or object, and `output` says whether it is read
   * from a response.
   */
  resolve(
    type: TypeRef,
    owner: string,
    output: boolean,
    where: string,
    description = "",
  ): DartType {
    switch (type.kind) {
      case "string":
        return type.format === "date-time" ? { kind: "dateTime" } : { kind: "string" };
      case "integer":
        // JSW-5: an int64 that can pass 2^53 - 1 is a string on the wire, so it never reaches here (MDL-4).
        return { kind: "int" };
      case "number":
        return { kind: "double" };
      case "boolean":
        return { kind: "bool" };
      case "unknown":
      case "null":
        return { kind: "any" };
      case "ref": {
        const schema = this.#schemas.get(type.name);
        if (schema === undefined)
          throw new GeneratorError(`${where}: unresolved schema ${type.name}.`);
        if (schema.type.kind === "enum") return { kind: "enum", name: schema.name };
        if (schema.type.kind === "object") return { kind: "class", name: schema.name };
        // A schema that is a list, a map, or a scalar is used as the type it names.
        return this.resolve(schema.type, schema.name, output, `Schema ${schema.name}`);
      }
      case "array":
        return { kind: "list", items: this.resolve(type.items, owner, output, where, description) };
      case "map":
        return {
          kind: "map",
          values: this.resolve(type.values, owner, output, where, description),
        };
      case "enum":
        this.#declare(
          {
            kind: "enum",
            name: owner,
            description: description === "" ? `The values of ${owner}.` : description,
            values: type.values,
          },
          where,
        );
        return { kind: "enum", name: owner };
      case "object":
        this.#declareClass(
          owner,
          description === "" ? `The ${owner} object.` : description,
          type.properties,
          output,
          where,
        );
        return { kind: "class", name: owner };
      case "union":
        throw new GeneratorError(
          `${where}: a oneOf with more than one type is not supported in Dart.`,
        );
      case "nullable": {
        const inner = this.resolve(type.inner, owner, output, where, description);
        return inner.kind === "any" || inner.kind === "nullable"
          ? inner
          : { kind: "nullable", inner };
      }
    }
  }

  /** A property's Dart type; a property the response may leave out is nullable. */
  property(property: Property, owner: string, output: boolean, where: string): DartType {
    const type = this.resolve(
      property.type,
      `${owner}${pascalCase(property.name)}`,
      output,
      where,
      property.description,
    );
    return property.required || type.kind === "nullable" || type.kind === "any"
      ? type
      : { kind: "nullable", inner: type };
  }

  /** The schema a `ref` names, for the generator's checks. */
  schema(name: string): NamedSchema | undefined {
    return this.#schemas.get(name);
  }

  get model(): TargetModel {
    return this.#model;
  }

  #declareClass(
    name: string,
    description: string,
    properties: Property[],
    output: boolean,
    where: string,
  ): void {
    this.#declare({ kind: "class", name, description, properties, output }, where);
    for (const property of properties) {
      this.property(property, name, output, `${where} property ${property.name}`);
    }
  }

  #declare(declaration: DartDeclaration, where: string): void {
    const shape = JSON.stringify(
      declaration.kind === "enum" ? declaration.values : declaration.properties,
    );
    const existing = this.#declarations.get(declaration.name);
    if (existing === undefined) {
      this.#declarations.set(declaration.name, { declaration, shape });
      return;
    }
    if (existing.shape !== shape || existing.declaration.kind !== declaration.kind) {
      throw new GeneratorError(
        `${where}: the Dart name ${declaration.name} is used by two different types.`,
      );
    }
    // The same inline type reached for input and output: keep what output needs.
    if (
      existing.declaration.kind === "class" &&
      declaration.kind === "class" &&
      declaration.output
    ) {
      existing.declaration.output = true;
    }
  }
}

/** True when the type holds an enum without passing through a class. */
export function holdsEnum(type: DartType): boolean {
  switch (type.kind) {
    case "enum":
      return true;
    case "list":
      return holdsEnum(type.items);
    case "map":
      return holdsEnum(type.values);
    case "nullable":
      return holdsEnum(type.inner);
    default:
      return false;
  }
}
