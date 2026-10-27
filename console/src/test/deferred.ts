/** A promise a test settles by hand, to hold a request open while it checks the pending state. */
export function deferred<Value>() {
  let resolve: ((value: Value) => void) | undefined;
  const promise = new Promise<Value>((settle) => {
    resolve = settle;
  });
  return {
    promise,
    resolve: (value: Value) => {
      resolve?.(value);
    },
  };
}
