// Spec 0014 CLT-2, XTN-1: in the public document, every operation carries x-orvano-sdk, x-orvano-audience, and
// x-orvano-errors. The console document (title "Orvano Console API") needs none of them.
const required = ["x-orvano-sdk", "x-orvano-audience", "x-orvano-errors"];

export default function orvanoPublicOperation(operation, _options, context) {
  if (context.document.data?.info?.title === "Orvano Console API") return [];
  if (operation === null || typeof operation !== "object") return [];
  return required
    .filter((name) => !(name in operation))
    .map((name) => ({ message: `A public operation needs ${name}.`, path: [...context.path] }));
}
