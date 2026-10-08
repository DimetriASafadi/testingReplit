/** Keep navigation and primary actions outside the scrolling content area. */
export const pageSize = () => window.innerHeight < 520 ? (window.innerWidth > window.innerHeight ? 2 : 1) : window.innerHeight < 740 ? 2 : 4;
export const dialogPageSize = () => window.innerHeight < 520 ? 1 : pageSize();
export function pageControls(page: number, pages: number, action: string, count: number) {
  if (pages < 2) return '';
  const n = (v: number) => v.toLocaleString('ar-EG');
  return `<nav class="page-controls" aria-label="صفحات الخيارات">
    <button class="btn ghost sm" data-a="${action}" data-id="${page - 1}" data-k="${action}-prev" ${page === 0 ? 'disabled' : ''}>السابق</button>
    <span><b class="num">${n(page + 1)} / ${n(pages)}</b><small>${n(count)} خيارًا</small></span>
    <button class="btn ghost sm" data-a="${action}" data-id="${page + 1}" data-k="${action}-next" ${page + 1 >= pages ? 'disabled' : ''}>التالي</button>
  </nav>`;
}
export function splitActions(html: string) {
  const root = document.createElement('div'); root.innerHTML = html;
  const actions: Element[] = [];
  for (const node of Array.from(root.children)) {
    if ((node.matches('button[data-a]') || node.matches('.row')) &&
        (node.matches('[data-a]') || node.querySelector('[data-a]'))) {
      actions.push(node); node.remove();
    }
  }
  return { body: root.innerHTML, footer: actions.map(n => n.outerHTML).join('') };
}
export function dialogLayout(html: string, tab: number, requestedPage: number) {
  const root = document.createElement('div'); root.innerHTML = html;
  const headers = Array.from(root.children).filter(n => n.tagName === 'H3');
  let tabs = '';
  if (headers.length > 1) {
    const selected = Math.max(0, Math.min(tab, headers.length - 1));
    tabs = `<nav class="window-tabs" aria-label="أقسام النافذة">${headers.map((h,i) =>
      `<button data-a="dialog-tab" data-id="${i}" data-k="dialog-tab-${i}" aria-pressed="${i === selected}">${h.innerHTML}</button>`).join('')}</nav>`;
    let section = -1;
    for (const child of Array.from(root.children)) {
      if (child.tagName === 'H3') section++;
      if (section >= 0 && (child.tagName === 'H3' || section !== selected)) child.remove();
    }
  }
  const list = root.querySelector('.list'), count = list?.children.length ?? 0;
  const size = dialogPageSize(), pages = Math.max(1, Math.ceil(count / size));
  const page = Math.max(0, Math.min(requestedPage, pages - 1));
  if (list && count > size) Array.from(list.children).forEach((row,i) => {
    if (i < page * size || i >= (page + 1) * size) row.remove();
  });
  const { body, footer } = splitActions(root.innerHTML);
  return { tabs, body, footer: footer + pageControls(page,pages,'dialog-page',count) };
}
