// Disposable CDP browser only. Restore its save; never connect to a user's phone.
import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b = await browser(390, 844), key = 'newgaza2d-save-v1';
let original, scriptId;
const reload = async () => {
  const origin = await b.evaluate('performance.timeOrigin');
  await b.send('Page.reload');
  // Ready in the OLD document is not evidence that the new document loaded.
  await b.until(`performance.timeOrigin > ${origin} && !!document.querySelector('#boot.done')`);
};
try {
  await b.send('Page.navigate', { url: 'http://localhost:80/' });
  await b.until(`!!document.querySelector('#boot.done')`);
  original = await b.evaluate(`localStorage.getItem('${key}')`);
  const script = await b.send('Page.addScriptToEvaluateOnNewDocument', { source: `
    const context = HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext = function(type, options) {
      if (type === 'webgl' || type === 'experimental-webgl' || type === 'webgl2')
        options = {...options, failIfMajorPerformanceCaveat:false};
      return context.call(this, type, options);
    };
    for (const Type of [window.WebGLRenderingContext, window.WebGL2RenderingContext]) {
      if (!Type) continue;
      const get = Type.prototype.getParameter, upload = Type.prototype.texImage2D;
      Type.prototype.getParameter = function(p) {
        return p === this.MAX_TEXTURE_SIZE ? 4096 : get.call(this, p);
      };
      Type.prototype.texImage2D = function(...args) {
        const image = args.length === 6 ? args[5] : null;
        if (image && (image.width > 4096 || image.height > 4096))
          throw new Error('Simulated mobile GPU texture limit: 4096');
        return upload.apply(this, args);
      };
    }
  ` });
  scriptId = script.identifier;
  await b.evaluate(`localStorage.removeItem('${key}');void 0`);
  await reload();
  await b.until(`!!document.querySelector('#boot.done') && !!document.querySelector('[data-a="enter"]')`);
  await b.click('[data-a="enter"]');
  await b.until(`window.__newgazaArtScene?.worldReady && document.querySelector('#city-status').hidden`);
  const terrain = await b.evaluate(`(()=>{
    const s=window.__newgazaArtScene;
    const tiles=keys=>keys.map(k=>{const c=s.textures.get(k).getSourceImage();return {w:c.width,h:c.height}});
    return {renderer:s.game.renderer.type,plots:s.views.size,frame:s.frame,
      ground:tiles(s.groundTextures),roads:tiles(s.roadTextures)};
  })()`);
  assert.equal(terrain.renderer, 2, 'Must test WebGL, not the headless Canvas fallback');
  assert.equal(terrain.plots, 144);
  for (const tiles of [terrain.ground, terrain.roads]) {
    assert.equal(tiles.length, 6);
    assert(tiles.every(t => t.w <= 2048 && t.h <= 2048));
    assert.equal(tiles.reduce((sum, t) => sum + t.w * t.h, 0), terrain.frame.w * terrain.frame.h);
  }
  assert.equal(b.errors.length, 0, JSON.stringify(b.errors));
  await b.screenshot('/tmp/newgaza-mobile-webgl-after-reset.png');
  await reload();
  await b.until(`window.__newgazaArtScene?.worldReady && document.querySelector('#city-status').hidden`);
  // An allocation failure must show recovery, not leave an unusable beige screen.
  const before = await b.evaluate(`JSON.parse(localStorage.getItem('${key}')).coins`);
  await b.evaluate(`(()=>{
    const s=window.__newgazaArtScene, add=s.textures.addCanvas.bind(s.textures);
    let once=true;
    s.textures.addCanvas=function(k,...args) {
      if(once && k.startsWith('ground:')){once=false;throw new Error('Test allocation failure')}
      return add(k,...args);
    };
    s.retryWorld();
  })()`);
  await b.until(`!!document.querySelector('[data-a="retry-city"]') && !document.querySelector('#city-status').hidden`);
  await b.click('[data-a="retry-city"]');
  await b.until(`window.__newgazaArtScene?.worldReady && document.querySelector('#city-status').hidden`);
  assert.equal(await b.evaluate(`JSON.parse(localStorage.getItem('${key}')).coins`), before);
  console.log('PASS: mobile WebGL 4096 limit, all 144 plots, tiled terrain/roads, saved reload, explicit render-failure recovery without resetting progress.');
} finally {
  if (scriptId) await b.send('Page.removeScriptToEvaluateOnNewDocument', { identifier: scriptId });
  if (original !== undefined) await b.evaluate(original === null
    ? `localStorage.removeItem('${key}');void 0`
    : `localStorage.setItem('${key}',${JSON.stringify(original)});void 0`);
  b.close();
}
