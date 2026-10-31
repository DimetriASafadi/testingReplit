import './performance-diagnostics';
// Keep startup failures visible even if importing Phaser/the game fails.
const message = document.getElementById('boot-msg');
const error = document.getElementById('boot-err');
let loaded = false;
function startupError(text: string) {
  if(document.getElementById('boot')?.classList.contains('done') || !error)return;
  if(message)message.textContent='لم يكتمل بدء اللعبة';
  error.hidden=false;error.replaceChildren(document.createTextNode(text));
  const retry=document.createElement('button');
  retry.textContent='إعادة المحاولة';retry.className='btn';
  retry.onclick=()=>location.reload();
  error.append(document.createElement('br'),retry);
}
if(message)message.textContent='تحميل محرك اللعبة…';
const waiting=window.setTimeout(()=>{
  if(!loaded)startupError('تأخر تحميل محرك اللعبة. تحقق من اتصال المعاينة ثم أعد المحاولة؛ لن يُحذف حفظك.');
},45000);
void import('./main').then(()=>{loaded=true;clearTimeout(waiting);}).catch(reason=>{
  clearTimeout(waiting);console.error('Game startup failed',reason);
  startupError('تعذر تشغيل اللعبة. '+(reason instanceof Error?reason.message:String(reason)));
});
