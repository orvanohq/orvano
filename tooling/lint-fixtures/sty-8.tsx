// lint-as: console/src/features/tables/schemas/rows-search.ts
import { z } from "zod";
import * as mini from "zod/mini";
import type { ZodType } from "zod/v4";

export const name = z.string();
export const size = mini.number();
export type Schema = ZodType;
