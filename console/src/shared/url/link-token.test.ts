import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { takeFragmentParam } from "./link-token";

// Spec 0003 URL-5, spec 0016 FRS-5: a token in a link's fragment is read once and leaves the address at once.

function open(address: string) {
  window.history.replaceState({ key: "router-state" }, "", address);
}

let startUrl = "";
beforeEach(() => {
  startUrl = `${window.location.pathname}${window.location.search}`;
});

afterEach(() => {
  window.history.replaceState(null, "", startUrl);
});

describe("takeFragmentParam", () => {
  it("returns the value once and drops the fragment", () => {
    open("#token=abc");

    expect(takeFragmentParam("token")).toBe("abc");
    expect(window.location.hash).toBe("");
    expect(window.location.href).not.toContain("abc");
    expect(takeFragmentParam("token")).toBeUndefined();
  });

  it("keeps the path, the query, and the router's history state", () => {
    open(`${startUrl}?step=1#token=abc`);

    takeFragmentParam("token");

    expect(window.location.search).toContain("step=1");
    expect(window.location.pathname).toBe(new URL(startUrl, window.location.href).pathname);
    expect(window.history.state).toEqual({ key: "router-state" });
  });

  it("keeps the other fragment parameters", () => {
    open("#from=mail&token=abc&lang=ar");

    expect(takeFragmentParam("token")).toBe("abc");
    expect(window.location.hash).toBe("#from=mail&lang=ar");
  });

  it("takes every copy of the name out of the address, and returns the first", () => {
    open("#token=first&token=second");

    expect(takeFragmentParam("token")).toBe("first");
    expect(window.location.hash).toBe("");
  });

  it("drops an empty value too, and returns it empty", () => {
    open("#token=");

    expect(takeFragmentParam("token")).toBe("");
    expect(window.location.hash).toBe("");
  });

  it("decodes the value", () => {
    open("#token=a%2Fb%3D");

    expect(takeFragmentParam("token")).toBe("a/b=");
  });

  it("leaves the address alone when the fragment has no such name", () => {
    open("#section=intro");

    expect(takeFragmentParam("token")).toBeUndefined();
    expect(window.location.hash).toBe("#section=intro");
  });

  it("matches the whole name, in its exact case", () => {
    open("#TOKEN=abc&mytoken=def");

    expect(takeFragmentParam("token")).toBeUndefined();
    expect(window.location.hash).toBe("#TOKEN=abc&mytoken=def");
  });

  it("returns nothing when there is no fragment", () => {
    open(startUrl);

    expect(takeFragmentParam("token")).toBeUndefined();
  });
});
