// Spec 0014 XTN-3, CLT-2: every code in an operation's x-orvano-errors is in the document's x-orvano-error-codes.
export default function orvanoCataloguedErrors(codes, _options, context) {
  if (!Array.isArray(codes)) return [{ message: "x-orvano-errors must be an array of codes." }];
  const catalogue = context.document.data?.["x-orvano-error-codes"] ?? {};
  return codes
    .map((code, index) => ({ code, index }))
    .filter(({ code }) => !Object.hasOwn(catalogue, code))
    .map(({ code, index }) => ({
      message: `x-orvano-errors lists "${code}", which x-orvano-error-codes does not declare.`,
      path: [...context.path, index],
    }));
}
