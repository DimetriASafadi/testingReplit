import { readdir, readFile, readlink, stat } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

export function listeningInodes(text, port) {
  return new Set(text.split('\n').flatMap(line => {
    const fields = line.trim().split(/\s+/);
    return fields[3] === '0A' && parseInt(fields[1]?.split(':').at(-1), 16) === port
      ? [fields[9]] : [];
  }));
}

export function isGameServer(args, cwd, root) {
  if (resolve(cwd) !== resolve(root)) return false;
  const entries = new Set([
    join(root, 'node_modules/vite/bin/vite.js'),
    join(root, 'scripts/dev-server.mjs'),
  ].map(p => resolve(p)));
  // The executable must be Node and its entrypoint must be this game's server.
  return /(^|\/)node$/.test(args[0] ?? '') && entries.has(resolve(cwd, args[1] ?? ''));
}

async function portInodes(port) {
  const tables = await Promise.all(['tcp', 'tcp6'].map(async name => {
    try { return await readFile(`/proc/net/${name}`, 'utf8'); }
    catch (e) { if (e.code === 'ENOENT') return ''; throw e; }
  }));
  return new Set(tables.flatMap(text => [...listeningInodes(text, port)]));
}

async function listenerPids(inodes) {
  const owners = [];
  for (const id of await readdir('/proc')) {
    if (!/^\d+$/.test(id)) continue;
    try {
      if ((await stat(`/proc/${id}`)).uid !== process.getuid()) continue;
      for (const fd of await readdir(`/proc/${id}/fd`)) {
        const link = await readlink(`/proc/${id}/fd/${fd}`);
        if (inodes.has(link.match(/^socket:\[(\d+)\]$/)?.[1])) {
          owners.push(Number(id)); break;
        }
      }
    } catch (e) {
      if (['ENOENT', 'ESRCH', 'EACCES'].includes(e.code)) continue;
      throw e;
    }
  }
  return owners;
}

/** Only supersede a verified previous server of this exact game.
 * Never change ports or stop another artifact to conceal a bind failure. */
export async function reclaimGamePort(root, port, timeoutMs = 8000) {
  const inodes = await portInodes(port);
  if (!inodes.size) return [];
  const owners = await listenerPids(inodes);
  if (!owners.length) throw new Error(`Port ${port} is occupied; its owner could not be verified.`);
  for (const pid of owners) {
    const cwd = await readlink(`/proc/${pid}/cwd`);
    const args = (await readFile(`/proc/${pid}/cmdline`, 'utf8')).split('\0');
    if (pid === process.pid || !isGameServer(args, cwd, root)) {
      throw new Error(`Port ${port} belongs to another process (${pid}); refusing to stop it.`);
    }
  }
  for (const pid of owners) {
    try { process.kill(pid, 'SIGTERM'); }
    catch (e) { if (e.code !== 'ESRCH') throw e; }
  }
  const deadline = Date.now() + timeoutMs;
  while ((await portInodes(port)).size) {
    if (Date.now() >= deadline) throw new Error(`Previous game server did not release port ${port}.`);
    await delay(100);
  }
  return owners;
}
