/** Update existing UI nodes so a ticking timer cannot remount an open panel. */
export function patchContent(parent: HTMLElement, html: string): void {
  const template = document.createElement('template');
  template.innerHTML = html;
  patchChildren(parent, template.content);
}

function patchChildren(parent: Node, next: Node): void {
  const wanted = Array.from(next.childNodes);
  wanted.forEach((node, index) => {
    const current = parent.childNodes[index];
    if (!current) { parent.appendChild(node.cloneNode(true)); return; }
    if (current.nodeType !== node.nodeType ||
        (current instanceof Element && node instanceof Element && current.tagName !== node.tagName)) {
      parent.replaceChild(node.cloneNode(true), current);
      return;
    }
    if (current instanceof Element && node instanceof Element) {
      for (const attr of Array.from(current.attributes)) {
        if (!node.hasAttribute(attr.name)) current.removeAttribute(attr.name);
      }
      for (const attr of Array.from(node.attributes)) {
        if (current.getAttribute(attr.name) !== attr.value) current.setAttribute(attr.name, attr.value);
      }
      patchChildren(current, node);
    } else if (current.nodeValue !== node.nodeValue) {
      current.nodeValue = node.nodeValue;
    }
  });
  while (parent.childNodes.length > wanted.length) parent.lastChild!.remove();
}
