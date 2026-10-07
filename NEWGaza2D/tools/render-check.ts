// Visual QA harness (in-memory state only; never touches localStorage). Open /render-check.html?mode=ruins|built|build|back&zoom=1&x=..&y=..
import Phaser from 'phaser';
import { BUILDINGS } from '../src/catalog';
import { newGame } from '../src/engine';
import { BootScene, CityScene } from '../src/scenes/CityScene';
const q = new URLSearchParams(location.search); const mode = q.get('mode') ?? 'ruins';
const st = newGame(); st.currentDistrict = 'shujaiya'; const d = st.districts[0]; const now = Date.now();
const bs = BUILDINGS;
d.plots.forEach((p, i) => {
  const b = bs[i % bs.length]; p.buildingId = b.id;
  if (mode === 'built' || mode === 'back') { p.status = 'built'; p.orientation = mode === 'back' ? 1 : 0; }
  else if (mode === 'build') { p.status = 'building'; p.orientation = (i % 2) as 0 | 1; const dur = 600000; const f = [0.1, 0.5, 0.85][i % 3]; p.startedAt = now - dur * f; p.endsAt = p.startedAt + dur; }
  else if (i % 7 === 3) p.status = 'empty';
});
if (q.get('zoom')) d.camera = { x: Number(q.get('x') ?? 0), y: Number(q.get('y') ?? 600), zoom: Number(q.get('zoom')) };
const game = new Phaser.Game({ type: Phaser.AUTO, parent: 'stage', backgroundColor: '#d9c39a', scale: { mode: Phaser.Scale.RESIZE, width: innerWidth, height: innerHeight }, scene: [BootScene, CityScene], input: { activePointers: 3 } });
game.registry.set('hooks', { getState: () => st, onSelect: () => {}, onCamera: () => {} });
(window as any).game = game;
const dbg = document.createElement('pre'); dbg.style.cssText = 'position:fixed;left:4px;top:4px;color:#000;font:11px monospace;z-index:9;margin:0'; document.body.appendChild(dbg);
window.addEventListener('error', e => { dbg.textContent += 'ERR ' + e.message + '\n'; });
setInterval(() => { const s = game.scene.getScene('city') as any; dbg.textContent = `active:${game.scene.isActive('city')} kids:${s?.children?.length} views:${s?.views?.size} cam:${s?.cameras?.main?.zoom?.toFixed(2)} ${Math.round(s?.cameras?.main?.midPoint?.x)},${Math.round(s?.cameras?.main?.midPoint?.y)} tex:${Object.keys(s?.textures?.list ?? {}).length}`; }, 500);
game.events.once('booted', () => setTimeout(() => (game.scene.getScene('city') as CityScene).sync(), 50));
setInterval(() => (game.scene.getScene('city') as CityScene).sync(), 1000);
