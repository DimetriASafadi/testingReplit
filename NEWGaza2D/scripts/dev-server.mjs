import { build, preview } from 'vite';
import { fileURLToPath } from 'node:url';
import { reclaimGamePort } from './dev-server-ownership.mjs';
import { withGameStartupLock } from './dev-server-startup-lock.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
const port = Number(process.env.PORT);
if (!Number.isInteger(port) || port < 1 || port > 65535) {
  throw new Error('A valid PORT must be supplied by the artifact workflow.');
}
if (!process.env.BASE_PATH?.startsWith('/')) {
  throw new Error('BASE_PATH must be supplied by the artifact workflow.');
}

let server;
let watcher;
async function shutdown() {
  const timeout = setTimeout(() => process.exit(1), 5000);
  timeout.unref();
  try { await watcher?.close(); await new Promise(resolve => server?.httpServer ? server.httpServer.close(resolve) : resolve()); process.exit(0); }
  catch (error) { console.error(error); process.exit(1); }
}
process.once('SIGTERM', shutdown);
process.once('SIGINT', shutdown);

await withGameStartupLock(root, port, async () => {
  const replaced = await reclaimGamePort(root, port);
  if (replaced.length) console.log(`Replaced previous game server: ${replaced.join(', ')}`);
  // Serve the same bundled modules used by the APK: the proxied preview should
  // not depend on hundreds of dev imports, a 6 MB unminified Phaser or HMR.
  watcher = await build({ root, build: {
    outDir: '.preview-dist', emptyOutDir: true,
    watch: { exclude: ['**/android/**', '**/dist/**', '**/.preview-dist/**', '**/node_modules/**'] },
  } });
  await new Promise((resolve, reject) => {
    watcher.on('event', event => {
      if(event.code === 'ERROR') { console.error(event.error); if(!server)reject(event.error); }
      if(event.code === 'END')resolve();
    });
  });
  server = await preview({ root, build: { outDir: '.preview-dist' } });
});
server.printUrls();
