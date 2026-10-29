// lint-as: console/src/features/tables/components/rows-page.tsx
declare const element: HTMLElement;

export const Raw = (): React.JSX.Element => (
  <div dangerouslySetInnerHTML={{ __html: "<b>x</b>" }} />
);
export const Handler = (): React.JSX.Element => <button onClick="alert(1)">x</button>;
export const run = (): unknown => eval("1 + 1");
export const make = (): unknown => new Function("return 1");
element.setAttribute("onclick", "alert(1)");
element.innerHTML = "<b>x</b>";
element.outerHTML += "<i>x</i>";
element["innerHTML"] = "<b>x</b>";
element.insertAdjacentHTML("beforeend", "<b>x</b>");
document.write("<b>x</b>");
