// C# types for the contract's schemas (spec 0002 LIB-9, LIB-11, spec 0014 GNR-6, JSW-1 to JSW-6). C# has no inline
// enums or object types, so one written inline in a property gets a type named after its owner and the property
// (`WidgetResponse` + `color` gives `WidgetResponseColor`). Unlike the Dart and TypeScript targets, request bodies
// are records of their own (spec 0002's canonical `CreateAsync(new CreateUserRequest { ... })`).
import { GeneratorError } from "../../contract.ts";
import type { NamedSchema, Operation, Property, TargetModel, TypeRef } from "../../model.ts";
import { byString, pascalCase } from "../../naming.ts";

export type CSharpType =
  | { kind: "string" }
  | { kind: "int" }
  | { kind: "long" }
  | { kind: "double" }
  | { kind: "bool" }
  | { kind: "guid" }
  | { kind: "dateTimeOffset" }
  | { kind: "any" }
  | { kind: "enum"; name: string }
  | { kind: "class"; name: string }
  | { kind: "list"; items: CSharpType }
  | { kind: "map"; values: CSharpType }
  | { kind: "nullable"; inner: CSharpType };

/** A C# enum struct or record the target emits. */
export type CSharpDeclaration =
  | { kind: "enum"; name: string; description: string; values: string[] }
  | {
      kind: "class";
      name: string;
      description: string;
      properties: Property[];
      /** Sent in a request: an optional property left null is not written (spec 0014 JSW-6). */
      input: boolean;
    };

/** Resolves contract types to C# types and collects every declaration the target emits. */
export class CSharpTypes {
  readonly #model: TargetModel;
  readonly #schemas: Map<string, NamedSchema>;
  readonly #declarations = new Map<string, { declaration: CSharpDeclaration; shape: string }>();

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
          use?.input === true,
          where,
        );
      }
    }
    for (const operation of model.services.flatMap((service) => service.operations)) {
      const body = requestBodyOf(operation);
      if (body !== undefined) {
        this.#declareClass(
          body.name,
          body.description,
          body.properties,
          true,
          `${operation.operationId} request body`,
        );
      }
    }
  }

  /** Every declaration, sorted by name. */
  declarations(): CSharpDeclaration[] {
    return [...this.#declarations.values()]
      .map((entry) => entry.declaration)
      .toSorted((a, b) => byString(a.name, b.name));
  }

  /** The C# type of a contract type. `owner` names an inline enum or object. */
  resolve(
    type: TypeRef,
    owner: string,
    input: boolean,
    where: string,
    description = "",
  ): CSharpType {
    switch (type.kind) {
      case "string":
        if (type.format === "uuid") return { kind: "guid" };
        return type.format === "date-time" ? { kind: "dateTimeOffset" } : { kind: "string" };
      case "integer":
        // JSW-5: an int64 that can pass 2^53 - 1 is a string on the wire, so it never reaches here.
        return type.format === "int64" ? { kind: "long" } : { kind: "int" };
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
        return this.resolve(schema.type, schema.name, input, `Schema ${schema.name}`);
      }
      case "array":
        return { kind: "list", items: this.resolve(type.items, owner, input, where, description) };
      case "map":
        return { kind: "map", values: this.resolve(type.values, owner, input, where, description) };
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
          input,
          where,
        );
        return { kind: "class", name: owner };
      case "union":
        throw new GeneratorError(
          `${where}: a oneOf with more than one type is not supported in C#.`,
        );
      case "nullable": {
        const inner = this.resolve(type.inner, owner, input, where, description);
        return inner.kind === "any" || inner.kind === "nullable"
          ? inner
          : { kind: "nullable", inner };
      }
    }
  }

  /** A property's C# type; a property that may be left out is nullable. */
  property(property: Property, owner: string, input: boolean, where: string): CSharpType {
    const type = this.resolve(
      property.type,
      `${owner}${pascalCase(property.name)}`,
      input,
      where,
      property.description,
    );
    return property.required || type.kind === "nullable" || type.kind === "any"
      ? type
      : { kind: "nullable", inner: type };
  }

  get model(): TargetModel {
    return this.#model;
  }

  #declareClass(
    name: string,
    description: string,
    properties: Property[],
    input: boolean,
    where: string,
  ): void {
    this.#declare({ kind: "class", name, description, properties, input }, where);
    for (const property of properties) {
      this.property(property, name, input, `${where} property ${property.name}`);
    }
  }

  #declare(declaration: CSharpDeclaration, where: string): void {
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
        `${where}: the C# name ${declaration.name} is used by two different types.`,
      );
    }
    // The same type reached as input and output: keep what a request needs.
    if (
      existing.declaration.kind === "class" &&
      declaration.kind === "class" &&
      declaration.input
    ) {
      existing.declaration.input = true;
    }
  }
}

/** An operation's request body as its own record: the named schema, else `<Method><Service>Request`. */
export function requestBodyOf(
  operation: Operation,
): { name: string; description: string; required: boolean; properties: Property[] } | undefined {
  const body = operation.body;
  if (body === undefined) return undefined;
  return {
    name: body.schema ?? `${pascalCase(operation.method)}${pascalCase(operation.service)}Request`,
    description:
      body.description === "" ? `The body of ${operation.operationId}.` : body.description,
    required: body.required,
    // A read only property is the server's to set (OpenAPI readOnly), so the request leaves it out.
    properties: body.properties.filter((property) => !property.readOnly),
  };
}
