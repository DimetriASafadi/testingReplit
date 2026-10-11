import { createServer } from 'vite';
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
async function shutdown() {
  const timeout = setTimeout(() => process.exit(1), 5000);
  timeout.unref();
  try { await server?.close(); process.exit(0); }
  catch (error) { console.error(error); process.exit(1); }
}
process.once('SIGTERM', shutdown);
process.once('SIGINT', shutdown);

await withGameStartupLock(root, port, async () => {
  const replaced = await reclaimGamePort(root, port);
  if (replaced.length) console.log(`Replaced previous game server: ${replaced.join(', ')}`);
  server = await createServer({ root });
  await server.listen();
});
server.printUrls();
