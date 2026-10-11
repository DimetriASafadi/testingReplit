import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { createConnection } from 'node:net';
import { listeningInodes, isGameServer, reclaimGamePort } from '../scripts/dev-server-ownership.mjs';
import { withGameStartupLock } from '../scripts/dev-server-startup-lock.mjs';
import { setTimeout as delay } from 'node:timers/promises';

test('only listening IPv4/IPv6 sockets on the requested port are selected', () => {
  const table = `sl local_address rem_address st tx_queue tr retrnsmt uid timeout inode
0: 00000000:4892 00000000:0000 0A 0:0 0:0 0 1000 0 123
1: 00000000:4892 00000000:0000 01 0:0 0:0 0 1000 0 456
2: 00000000000000000000000000000000:4892 0:0 0A 0:0 0:0 0 1000 0 789
3: 00000000:1F91 0:0 0A 0:0 0:0 0 1000 0 999`;
  assert.deepEqual([...listeningInodes(table, 18578)], ['123', '789']);
});
test('takeover requires the exact game directory and Node server entrypoint', () => {
  const root = '/workspace/NEWGaza2D';
  assert.ok(isGameServer(['/bin/node', './scripts/dev-server.mjs'], root, root));
  assert.ok(isGameServer(['/bin/node', './node_modules/.bin/../vite/bin/vite.js'], root, root));
  assert.equal(isGameServer(['/bin/node', './scripts/dev-server.mjs'], '/workspace/other', root), false);
  assert.equal(isGameServer(['/bin/node', './scripts/other.mjs'], root, root), false);
  assert.equal(isGameServer(['/bin/bash', './scripts/dev-server.mjs'], root, root), false);
});
async function fixture(name, run) {
  const root = await mkdtemp(join(tmpdir(), 'newgaza-server-'));
  await mkdir(join(root, 'scripts'));
  const file = join(root, 'scripts', name);
  await writeFile(file, `import {createServer} from 'node:net';const s=createServer();s.listen(0,'0.0.0.0',()=>console.log(s.address().port));`);
  const child = spawn(process.execPath, [file], { cwd: root, stdio: ['ignore', 'pipe', 'pipe'] });
  try {
    const [output] = await once(child.stdout, 'data');
    await run(root, Number(String(output).trim()), child);
  } finally {
    if (child.exitCode === null && child.signalCode === null) {
      const exited = once(child, 'exit'); child.kill(); await exited;
    }
    await rm(root, { recursive: true, force: true });
  }
}
test('a verified previous game instance releases its port before the new start', async () => {
  await fixture('dev-server.mjs', async (root, port, child) => {
    const exited = once(child, 'exit');
    assert.deepEqual(await reclaimGamePort(root, port), [child.pid]);
    await exited;
    assert.deepEqual(await reclaimGamePort(root, port), []);
  });
});
test('an unrelated listener remains alive and available after a refused takeover', async () => {
  await fixture('other.mjs', async (root, port, child) => {
    await assert.rejects(reclaimGamePort(root, port), /belongs to another process/);
    assert.equal(child.signalCode, null);
    const socket = createConnection({ host: '127.0.0.1', port });
    await once(socket, 'connect'); socket.destroy();
  });
});

test('concurrent game starts serialize ownership checks through port binding', async () => {
  const root = await mkdtemp(join(tmpdir(), 'newgaza-lock-'));
  let active = 0, peak = 0, completed = 0;
  try {
    await Promise.all(Array.from({ length: 4 }, () =>
      withGameStartupLock(root, 18578, async () => {
        peak = Math.max(peak, ++active);
        await delay(60);
        --active; ++completed;
      })));
    assert.equal(peak, 1);
    assert.equal(completed, 4);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('failed startup releases the lock for the next attempt', async () => {
  const root = await mkdtemp(join(tmpdir(), 'newgaza-lock-'));
  try {
    await assert.rejects(withGameStartupLock(root, 18578, async () => {
      throw new Error('fixture startup failure');
    }), /fixture startup failure/);
    assert.equal(await withGameStartupLock(root, 18578, async () => 'started'), 'started');
  } finally { await rm(root, { recursive: true, force: true }); }
});
