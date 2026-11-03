/** Real celebration markup/resource helpers, without changing player saves. */
import { celebrationMarkup } from '../../src/expansion-ui';
import { gemIconHtml } from '../../src/gem-icon';
import '../../src/style.css';
import '../../src/expansion-ui.css';
document.documentElement.dir='rtl';
document.body.innerHTML=`<div class="wallet" style="position:fixed;top:12px;right:8px;left:8px;justify-content:center">
<span class="pill"><img class="resource-icon resource-coins" src="/art/ui-resources/coins.webp" alt="">497,000</span>
<span class="pill">${gemIconHtml()}30</span>
<span class="pill"><img class="resource-icon resource-concrete" src="/art/ui-resources/concrete.webp" alt="">0 إسمنت</span>
<span class="pill"><img class="resource-icon resource-iron" src="/art/ui-resources/iron.webp" alt="">0 حديد</span></div>`;
if(!new URLSearchParams(location.search).has('hud')){
  const dialog=document.createElement('dialog');dialog.className='celebration still';
  dialog.innerHTML=celebrationMarkup('الزيتون',100000,10,true);
  document.body.append(dialog);dialog.showModal();
}
