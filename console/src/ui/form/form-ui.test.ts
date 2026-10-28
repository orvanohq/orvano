import { describe, expect, it } from "vitest";
import { firstMessage } from "./form-ui";

// Spec 0003 FRM-3, FRM-4: the one message a field shows, from TanStack Form's mix of server strings and Standard
// Schema issues.

describe("firstMessage", () => {
  it("0003 FRM-4: a server error string is the message", () => {
    expect(firstMessage(["That name is taken."])).toBe("That name is taken.");
  });

  it("0003 FRM-3: a schema issue's message is the message", () => {
    expect(firstMessage([{ message: "Use at most 10 MB.", path: ["size"] }])).toBe(
      "Use at most 10 MB.",
    );
  });

  it("0003 FRM-3: finds the message inside a nested list of issues", () => {
    expect(firstMessage([[undefined, [{ message: "Choose a region." }]]])).toBe("Choose a region.");
  });

  it("skips empty, null, and messageless entries for the first real one", () => {
    expect(
      firstMessage(["", null, undefined, 0, {}, { message: "" }, { message: 42 }, [], "Second."]),
    ).toBe("Second.");
  });

  it("returns undefined when there is nothing to show", () => {
    expect(firstMessage([])).toBeUndefined();
    expect(firstMessage([undefined, "", [[]], { message: "" }])).toBeUndefined();
  });

  it("keeps the order: the first message wins", () => {
    expect(firstMessage([{ message: "First." }, "Second."])).toBe("First.");
  });
});
